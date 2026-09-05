using System.Text.Json;
using AiCsServer.Dashboard;
using AiCsServer.Data;
using AiCsServer.Tools;

namespace AiCsServer.Routes;

public static class DashboardAgUiEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static void MapDashboardAgUi(this WebApplication app)
    {
        app.MapPost("/dashboard/compile", CompileOnlyAsync);
    }

    internal static async Task<bool> TryWriteCachedDashboardSseAsync(HttpContext context, JsonElement root)
    {
        string threadId = root.TryGetProperty("threadId", out JsonElement threadEl)
            ? threadEl.GetString() ?? Guid.NewGuid().ToString("N")
            : Guid.NewGuid().ToString("N");
        string runId = root.TryGetProperty("runId", out JsonElement runEl)
            ? runEl.GetString() ?? Guid.NewGuid().ToString("N")
            : Guid.NewGuid().ToString("N");
        bool preventCaching = IsPreventCachingRequested(root);
        string? catalogId = ExtractCatalogId(root);

        List<JsonElement> messages = new();
        if (root.TryGetProperty("messages", out JsonElement messagesEl) && messagesEl.ValueKind == JsonValueKind.Array)
        {
            messages.AddRange(messagesEl.EnumerateArray());
        }

        if (preventCaching)
        {
            return false;
        }

        string cacheKey = DashboardCacheStore.ComputeRequestHash(messages);
        DashboardSpec? cached = await DashboardCacheStore.ReadAsync(cacheKey).ConfigureAwait(false);
        if (cached is null)
        {
            return false;
        }

        await WriteCachedSseAsync(context, threadId, runId, cached, catalogId).ConfigureAwait(false);
        return true;
    }

    private static async Task CompileOnlyAsync(HttpContext context)
    {
        using JsonDocument? body = await JsonDocument.ParseAsync(context.Request.Body).ConfigureAwait(false);
        if (body is null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        DashboardSpec? spec = JsonSerializer.Deserialize<DashboardSpec>(body.RootElement.GetRawText(), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        if (spec is null || spec.Tiles.Count == 0)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("invalid dashboard spec").ConfigureAwait(false);
            return;
        }

        string? catalogId = null;
        if (body.RootElement.TryGetProperty("catalogId", out JsonElement catalogEl))
        {
            catalogId = catalogEl.GetString();
        }

        CompiledDashboard compiled = await CompileDashboard.CompileAsync(spec, catalogId: catalogId)
            .ConfigureAwait(false);

        bool writeCache = true;
        if (body.RootElement.TryGetProperty("cacheKey", out JsonElement cacheKeyEl) &&
            cacheKeyEl.ValueKind == JsonValueKind.String)
        {
            string? key = cacheKeyEl.GetString();
            if (!string.IsNullOrWhiteSpace(key))
            {
                await DashboardCacheStore.WriteAsync(key, spec).ConfigureAwait(false);
                writeCache = false;
            }
        }

        if (writeCache && body.RootElement.TryGetProperty("messages", out JsonElement messagesEl) &&
            messagesEl.ValueKind == JsonValueKind.Array)
        {
            string hash = DashboardCacheStore.ComputeRequestHash(messagesEl.EnumerateArray());
            await DashboardCacheStore.WriteAsync(hash, spec).ConfigureAwait(false);
        }

        await context.Response.WriteAsJsonAsync(new
        {
            ok = true,
            surfaceId = compiled.SurfaceId,
            operations = compiled.Structural.Concat(compiled.DataModel).ToArray(),
            dataSteps = compiled.DataSteps,
            activitySnapshot = new
            {
                type = "ACTIVITY_SNAPSHOT",
                messageId = compiled.SurfaceId,
                activityType = "a2ui-surface",
                content = new
                {
                    operations = compiled.Structural.Concat(compiled.DataModel).ToArray(),
                },
            },
        }, JsonOptions).ConfigureAwait(false);
    }

    private static async Task WriteCachedSseAsync(
        HttpContext context,
        string threadId,
        string runId,
        DashboardSpec spec,
        string? catalogId)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        async Task Emit(object payload)
        {
            string json = JsonSerializer.Serialize(payload, JsonOptions);
            await context.Response.WriteAsync($"data: {json}\n\n").ConfigureAwait(false);
            await context.Response.Body.FlushAsync().ConfigureAwait(false);
        }

        await Emit(new { type = "RUN_STARTED", threadId, runId }).ConfigureAwait(false);

        string renderToolCallId = Guid.NewGuid().ToString("N");
        string parentMessageId = Guid.NewGuid().ToString("N");
        await Emit(new
        {
            type = "TOOL_CALL_START",
            parentMessageId,
            toolCallId = renderToolCallId,
            toolCallName = DashboardTools.RenderDashboardToolName,
        }).ConfigureAwait(false);
        await Emit(new
        {
            type = "TOOL_CALL_ARGS",
            toolCallId = renderToolCallId,
            delta = JsonSerializer.Serialize(spec, JsonOptions),
        }).ConfigureAwait(false);
        await Emit(new { type = "TOOL_CALL_END", toolCallId = renderToolCallId }).ConfigureAwait(false);

        CompiledDashboard compiled = await CompileDashboard.CompileAsync(spec, catalogId: catalogId)
            .ConfigureAwait(false);

        foreach (DataStep step in compiled.DataSteps)
        {
            string toolCallId = $"data-step-{Guid.NewGuid():N}";
            string stepParent = Guid.NewGuid().ToString("N");
            await Emit(new
            {
                type = "TOOL_CALL_START",
                parentMessageId = stepParent,
                toolCallId,
                toolCallName = step.Name,
            }).ConfigureAwait(false);
            await Emit(new
            {
                type = "TOOL_CALL_ARGS",
                toolCallId,
                delta = JsonSerializer.Serialize(step.Args ?? new { }, JsonOptions),
            }).ConfigureAwait(false);
            await Emit(new { type = "TOOL_CALL_END", toolCallId }).ConfigureAwait(false);
            await Emit(new
            {
                type = "TOOL_CALL_RESULT",
                toolCallId,
                content = JsonSerializer.Serialize(step.Result ?? new { ok = true }, JsonOptions),
                messageId = Guid.NewGuid().ToString("N"),
                role = "tool",
            }).ConfigureAwait(false);
        }

        await Emit(new
        {
            type = "ACTIVITY_SNAPSHOT",
            messageId = compiled.SurfaceId,
            activityType = "a2ui-surface",
            content = new
            {
                operations = compiled.Structural.Concat(compiled.DataModel).ToArray(),
            },
        }).ConfigureAwait(false);

        await Emit(new
        {
            type = "TOOL_CALL_RESULT",
            toolCallId = renderToolCallId,
            content = JsonSerializer.Serialize(new { ok = true, cached = true }, JsonOptions),
            messageId = Guid.NewGuid().ToString("N"),
            role = "tool",
        }).ConfigureAwait(false);

        await Emit(new { type = "RUN_FINISHED", threadId, runId }).ConfigureAwait(false);
    }

    private static bool IsPreventCachingRequested(JsonElement root)
    {
        if (!root.TryGetProperty("forwardedProps", out JsonElement props) ||
            props.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!props.TryGetProperty("preventCaching", out JsonElement value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => value.GetString()?.ToLowerInvariant() is "1" or "true" or "yes",
            _ => false,
        };
    }

    private static string? ExtractCatalogId(JsonElement root)
    {
        if (!root.TryGetProperty("context", out JsonElement context) || context.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement entry in context.EnumerateArray())
        {
            if (!entry.TryGetProperty("value", out JsonElement value) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? raw = value.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("catalogId", out JsonElement catalogId))
                {
                    return catalogId.GetString();
                }
            }
            catch
            {
            }
        }

        return null;
    }
}
