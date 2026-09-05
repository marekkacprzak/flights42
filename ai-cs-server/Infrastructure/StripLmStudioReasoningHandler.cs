using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiCsServer.Infrastructure;

public sealed class StripLmStudioReasoningHandler : DelegatingHandler
{
    public StripLmStudioReasoningHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (response.Content is null)
        {
            return response;
        }

        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            Stream original = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            response.Content = new StreamContent(new ReasoningStrippingSseStream(original));
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return response;
        }

        if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            response.Content = new StringContent(
                StripReasoningFromJson(body),
                Encoding.UTF8,
                "application/json");
        }

        return response;
    }

    internal static string StripReasoningFromJson(string json)
    {
        JsonNode? node = JsonNode.Parse(json);
        if (node is null)
        {
            return json;
        }

        StripReasoning(node);
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    internal static bool StripReasoning(JsonNode node)
    {
        if (node is not JsonObject root)
        {
            return false;
        }

        if (root["choices"] is not JsonArray choices)
        {
            return false;
        }

        bool changed = false;
        foreach (JsonNode? choiceNode in choices)
        {
            if (choiceNode is not JsonObject choice)
            {
                continue;
            }

            if (choice["delta"] is JsonObject delta && delta.Remove("reasoning_content"))
            {
                changed = true;
            }

            if (choice["message"] is JsonObject message && message.Remove("reasoning_content"))
            {
                changed = true;
            }
        }

        return changed;
    }

    internal static bool ShouldDropChunk(JsonNode node)
    {
        if (node is not JsonObject root)
        {
            return false;
        }

        if (root["choices"] is not JsonArray choices || choices.Count == 0)
        {
            return true;
        }

        if (choices[0] is not JsonObject choice || choice["delta"] is not JsonObject delta)
        {
            return false;
        }

        if (choice["finish_reason"] is JsonValue finish && finish.GetValueKind() != JsonValueKind.Null)
        {
            return false;
        }

        foreach (KeyValuePair<string, JsonNode?> property in delta)
        {
            if (property.Key is "role" or "content" or "tool_calls" or "function_call" or "refusal")
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class ReasoningStrippingSseStream : Stream
{
    private readonly Stream _inner;
    private readonly Queue<byte[]> _pending = new();
    private byte[]? _current;
    private int _currentOffset;
    private readonly MemoryStream _lineBuffer = new();
    private bool _done;

    public ReasoningStrippingSseStream(Stream inner)
    {
        _inner = inner;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (_current is not null)
            {
                int available = _current.Length - _currentOffset;
                int toCopy = Math.Min(available, buffer.Length);
                _current.AsSpan(_currentOffset, toCopy).CopyTo(buffer.Span);
                _currentOffset += toCopy;
                if (_currentOffset >= _current.Length)
                {
                    _current = null;
                    _currentOffset = 0;
                }

                return toCopy;
            }

            if (_pending.Count > 0)
            {
                _current = _pending.Dequeue();
                _currentOffset = 0;
                continue;
            }

            if (_done)
            {
                return 0;
            }

            byte[] readBuffer = new byte[4096];
            int read = await _inner.ReadAsync(readBuffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                FlushLineBuffer();
                _done = true;
                continue;
            }

            for (int i = 0; i < read; i++)
            {
                byte b = readBuffer[i];
                if (b == 0x0A)
                {
                    EmitTransformedLine();
                    _lineBuffer.SetLength(0);
                }
                else if (b != 0x0D)
                {
                    _lineBuffer.WriteByte(b);
                }
            }
        }
    }

    private void FlushLineBuffer()
    {
        if (_lineBuffer.Length == 0)
        {
            return;
        }

        EmitTransformedLine();
        _lineBuffer.SetLength(0);
    }

    private void EmitTransformedLine()
    {
        string line = Encoding.UTF8.GetString(_lineBuffer.GetBuffer(), 0, (int)_lineBuffer.Length);
        if (line.StartsWith("data:", StringComparison.Ordinal))
        {
            string payload = line[5..].TrimStart();
            if (payload.Length > 0 && payload != "[DONE]")
            {
                try
                {
                    JsonNode? node = JsonNode.Parse(payload);
                    if (node is not null)
                    {
                        StripLmStudioReasoningHandler.StripReasoning(node);
                        if (StripLmStudioReasoningHandler.ShouldDropChunk(node))
                        {
                            return;
                        }

                        line = "data: " + node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
                    }
                }
                catch (JsonException)
                {
                }
            }
        }

        _pending.Enqueue(Encoding.UTF8.GetBytes(string.Concat(line, ((char)10).ToString())));
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            _lineBuffer.Dispose();
        }

        base.Dispose(disposing);
    }
}
