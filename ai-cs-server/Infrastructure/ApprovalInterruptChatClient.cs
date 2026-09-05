using System.Collections.Concurrent;
using System.Text.Json;
using AGUI.Abstractions;
using AiCsServer.Data;
using AiCsServer.Tools;
using Microsoft.Extensions.AI;

namespace AiCsServer.Infrastructure;

public sealed class ApprovalInterruptChatClient : DelegatingChatClient
{
    private const string BookFlightName = "bookFlight";
    private const string CancelFlightName = "cancelFlight";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonElement BookResponseSchema = JsonDocument.Parse(
        """{"type":"object","properties":{"selection":{"type":"string","enum":["creditCard","miles","cancel"]}},"required":["selection"]}""")
        .RootElement.Clone();

    private static readonly JsonElement CancelResponseSchema = JsonDocument.Parse(
        """{"type":"object","properties":{"approved":{"type":"boolean"}},"required":["approved"]}""")
        .RootElement.Clone();

    private static readonly ConcurrentDictionary<string, PendingApproval> Pending = new(StringComparer.Ordinal);

    public ApprovalInterruptChatClient(IChatClient innerClient)
        : base(innerClient)
    {
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        IList<ChatMessage> prepared = await PrepareMessagesAsync(messages, cancellationToken).ConfigureAwait(false);
        ChatResponse response = await base.GetResponseAsync(prepared, options, cancellationToken).ConfigureAwait(false);
        if (!FeatureFlags.UseApproval)
        {
            return response;
        }

        List<ChatMessage> rewrittenMessages = [];
        foreach (ChatMessage message in response.Messages)
        {
            rewrittenMessages.Add(await RewriteOutgoingMessageAsync(message, cancellationToken).ConfigureAwait(false));
        }

        return new ChatResponse(rewrittenMessages)
        {
            ResponseId = response.ResponseId,
            ConversationId = response.ConversationId,
            ModelId = response.ModelId,
            CreatedAt = response.CreatedAt,
            FinishReason = response.FinishReason,
            Usage = response.Usage,
            AdditionalProperties = response.AdditionalProperties,
            RawRepresentation = response.RawRepresentation,
        };
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IList<ChatMessage> prepared = await PrepareMessagesAsync(messages, cancellationToken).ConfigureAwait(false);
        await foreach (ChatResponseUpdate update in base.GetStreamingResponseAsync(prepared, options, cancellationToken))
        {
            if (!FeatureFlags.UseApproval)
            {
                yield return update;
                continue;
            }

            yield return await RewriteOutgoingUpdateAsync(update, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IList<ChatMessage>> PrepareMessagesAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        List<ChatMessage> list = messages as List<ChatMessage> ?? messages.ToList();
        if (!FeatureFlags.UseApproval)
        {
            return list;
        }

        Dictionary<string, CompletedApproval> completed = new(StringComparer.Ordinal);

        List<ChatMessage> withResponses = [];
        foreach (ChatMessage message in list)
        {
            List<AIContent>? contents = null;
            foreach (AIContent content in message.Contents)
            {
                if (content is InterruptResponseContent response
                    && Pending.TryRemove(response.RequestId, out PendingApproval? pending))
                {
                    object result = await ExecutePendingAsync(pending, response.Payload, cancellationToken)
                        .ConfigureAwait(false);
                    completed[pending.CallId] = new CompletedApproval(pending, result);
                    contents ??= CopyContentsExcept(message.Contents, content);
                    contents.Add(new FunctionResultContent(pending.CallId, result));
                }
            }

            if (contents is null)
            {
                withResponses.Add(message);
            }
            else if (contents.Count > 0)
            {
                withResponses.Add(CloneMessage(message, contents));
            }
        }

        if (completed.Count == 0)
        {
            return withResponses;
        }

        List<ChatMessage> finalMessages = [];
        foreach (ChatMessage message in withResponses)
        {
            List<AIContent>? contents = null;
            foreach (AIContent content in message.Contents)
            {
                if (content is InterruptRequestContent request
                    && completed.TryGetValue(request.RequestId, out CompletedApproval? done))
                {
                    contents ??= CopyContentsExcept(message.Contents, content);
                    contents.Add(new FunctionCallContent(done.Pending.CallId, done.Pending.Name, done.Pending.Arguments));
                }
            }

            if (contents is null)
            {
                finalMessages.Add(message);
            }
            else if (contents.Count > 0)
            {
                finalMessages.Add(CloneMessage(message, contents));
            }
        }

        return finalMessages;
    }

    private static ChatMessage CloneMessage(ChatMessage message, IList<AIContent> contents)
    {
        return new ChatMessage(message.Role, contents)
        {
            AuthorName = message.AuthorName,
            MessageId = message.MessageId,
            AdditionalProperties = message.AdditionalProperties,
            RawRepresentation = message.RawRepresentation,
        };
    }

    private static List<AIContent> CopyContentsExcept(IList<AIContent> contents, AIContent exclude)
    {
        List<AIContent> copy = [];
        foreach (AIContent content in contents)
        {
            if (!ReferenceEquals(content, exclude))
            {
                copy.Add(content);
            }
        }

        return copy;
    }

    private static async Task<ChatMessage> RewriteOutgoingMessageAsync(
        ChatMessage message,
        CancellationToken cancellationToken)
    {
        List<AIContent> contents = [];
        bool changed = false;
        foreach (AIContent content in message.Contents)
        {
            if (content is FunctionCallContent call
                && IsApprovalTool(call.Name))
            {
                InterruptRequestContent? interrupt = await TryCreateInterruptAsync(call, cancellationToken)
                    .ConfigureAwait(false);
                if (interrupt is not null)
                {
                    contents.Add(interrupt);
                    changed = true;
                    continue;
                }
            }

            contents.Add(content);
        }

        if (!changed)
        {
            return message;
        }

        return CloneMessage(message, contents);
    }

    private static async Task<ChatResponseUpdate> RewriteOutgoingUpdateAsync(
        ChatResponseUpdate update,
        CancellationToken cancellationToken)
    {
        List<AIContent> contents = [];
        bool changed = false;
        foreach (AIContent content in update.Contents)
        {
            if (content is FunctionCallContent call
                && IsApprovalTool(call.Name))
            {
                InterruptRequestContent? interrupt = await TryCreateInterruptAsync(call, cancellationToken)
                    .ConfigureAwait(false);
                if (interrupt is not null)
                {
                    contents.Add(interrupt);
                    changed = true;
                    continue;
                }
            }

            contents.Add(content);
        }

        if (!changed)
        {
            return update;
        }

        return new ChatResponseUpdate
        {
            AuthorName = update.AuthorName,
            Role = update.Role,
            Contents = contents,
            ResponseId = update.ResponseId,
            MessageId = update.MessageId,
            ConversationId = update.ConversationId,
            CreatedAt = update.CreatedAt,
            FinishReason = update.FinishReason,
            ModelId = update.ModelId,
            AdditionalProperties = update.AdditionalProperties,
            RawRepresentation = update.RawRepresentation,
        };
    }

    private static bool IsApprovalTool(string? name)
    {
        return name == BookFlightName || name == CancelFlightName;
    }

    private static async Task<InterruptRequestContent?> TryCreateInterruptAsync(
        FunctionCallContent call,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (!TryGetFlightId(call.Arguments, out int flightId))
        {
            return null;
        }

        if (call.Name == BookFlightName)
        {
            if (BookedFlightsStore.IsBooked(flightId))
            {
                return null;
            }

            BookedFlight? flight = await BookedFlightsStore.FetchFlightAsync(flightId).ConfigureAwait(false);
            if (flight is null)
            {
                return null;
            }

            string message =
                $"How would you like to pay for flight {flightId} from {flight.From} to {flight.To} on {FormatDate.FormatFlightDate(flight.Date)}?";

            object suspendPayload = new
            {
                action = "book",
                flightId,
                flight,
                message,
                options = new object[]
                {
                    new { id = "creditCard", label = "Pay with credit card", payload = new { selection = "creditCard" }, variant = "default" },
                    new { id = "miles", label = "Pay with bonus miles", payload = new { selection = "miles" }, variant = "default" },
                    new { id = "cancel", label = "Cancel", payload = new { selection = "cancel" }, variant = "default" },
                },
            };

            return StoreAndCreate(call, message, BookResponseSchema, suspendPayload, new { flightId });
        }

        if (call.Name == CancelFlightName)
        {
            if (!BookedFlightsStore.IsBooked(flightId))
            {
                return null;
            }

            BookedFlight? flight = null;
            try
            {
                flight = await BookedFlightsStore.FetchFlightAsync(flightId).ConfigureAwait(false);
            }
            catch
            {
            }

            string flightContext = flight is null
                ? ""
                : $" from {flight.From} to {flight.To} on {FormatDate.FormatFlightDate(flight.Date)}";
            string message = $"Cancel flight {flightId}{flightContext}?";

            object suspendPayload = new
            {
                action = "cancel",
                flightId,
                flight,
                message,
                options = new object[]
                {
                    new { id = "accept", label = "Accept", payload = new { approved = true }, variant = "default" },
                    new { id = "decline", label = "Decline", payload = new { approved = false }, variant = "default" },
                },
            };

            return StoreAndCreate(call, message, CancelResponseSchema, suspendPayload, new { flightId });
        }

        return null;
    }

    private static InterruptRequestContent StoreAndCreate(
        FunctionCallContent call,
        string message,
        JsonElement responseSchema,
        object suspendPayload,
        object args)
    {
        string requestId = string.IsNullOrWhiteSpace(call.CallId) ? Guid.NewGuid().ToString("N") : call.CallId;
        string callId = requestId;
        Dictionary<string, object?> argumentMap = new(StringComparer.Ordinal);
        if (call.Arguments is not null)
        {
            foreach (KeyValuePair<string, object?> pair in call.Arguments)
            {
                argumentMap[pair.Key] = pair.Value;
            }
        }

        Pending[requestId] = new PendingApproval(requestId, callId, call.Name, argumentMap);

        JsonElement metadata = JsonSerializer.SerializeToElement(new
        {
            kind = "tool_suspended",
            toolName = call.Name,
            args,
            suspendPayload,
        }, JsonOptions);

        return new InterruptRequestContent(requestId)
        {
            Reason = "tool_suspended",
            Message = message,
            ToolCallId = callId,
            ResponseSchema = responseSchema.Clone(),
            Metadata = metadata,
        };
    }

    private static async Task<object> ExecutePendingAsync(
        PendingApproval pending,
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (!TryGetFlightId(pending.Arguments, out int flightId))
        {
            return new FlightTools.BookFlightFailure(false, "Missing flightId on resume.", "INVALID_RESUME");
        }

        if (pending.Name == BookFlightName)
        {
            string selection = "creditCard";
            if (payload is JsonElement el && el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("selection", out JsonElement selectionEl)
                    && selectionEl.ValueKind == JsonValueKind.String)
                {
                    selection = selectionEl.GetString() ?? selection;
                }
            }

            return await FlightTools.CompleteBookFlightAsync(flightId, selection).ConfigureAwait(false);
        }

        if (pending.Name == CancelFlightName)
        {
            bool approved = false;
            if (payload is JsonElement el && el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("approved", out JsonElement approvedEl)
                    && (approvedEl.ValueKind == JsonValueKind.True || approvedEl.ValueKind == JsonValueKind.False))
                {
                    approved = approvedEl.GetBoolean();
                }
            }

            return await FlightTools.CompleteCancelFlightAsync(flightId, approved).ConfigureAwait(false);
        }

        return new FlightTools.BookFlightFailure(false, $"Unknown tool {pending.Name}.", "UNKNOWN_TOOL");
    }

    private static bool TryGetFlightId(IDictionary<string, object?>? arguments, out int flightId)
    {
        flightId = 0;
        if (arguments is null)
        {
            return false;
        }

        if (!arguments.TryGetValue("flightId", out object? raw) && !arguments.TryGetValue("FlightId", out raw))
        {
            return false;
        }

        switch (raw)
        {
            case int i:
                flightId = i;
                return true;
            case long l:
                flightId = (int)l;
                return true;
            case JsonElement je when je.TryGetInt32(out int fromJson):
                flightId = fromJson;
                return true;
            case string s when int.TryParse(s, out int parsed):
                flightId = parsed;
                return true;
            default:
                return false;
        }
    }

    private sealed record PendingApproval(
        string RequestId,
        string CallId,
        string Name,
        IDictionary<string, object?> Arguments);

    private sealed record CompletedApproval(PendingApproval Pending, object Result);
}
