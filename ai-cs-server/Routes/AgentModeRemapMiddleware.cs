using System.Text;
using System.Text.Json;

namespace AiCsServer.Routes;

public sealed class AgentModeRemapMiddleware
{
    private readonly RequestDelegate _next;

    public AgentModeRemapMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsPost(context.Request.Method) &&
            context.Request.Path.Equals("/ag-ui/ticketingAgent", StringComparison.OrdinalIgnoreCase))
        {
            context.Request.EnableBuffering();
            using StreamReader reader = new(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            string body = await reader.ReadToEndAsync().ConfigureAwait(false);
            context.Request.Body.Position = 0;

            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("forwardedProps", out JsonElement forwarded) &&
                        forwarded.ValueKind == JsonValueKind.Object &&
                        forwarded.TryGetProperty("agentMode", out JsonElement modeElement) &&
                        modeElement.ValueKind == JsonValueKind.String)
                    {
                        string? mode = modeElement.GetString();
                        if (string.Equals(mode, "plan", StringComparison.Ordinal))
                        {
                            context.Request.Path = "/ag-ui/planningAgent";
                        }
                        else if (string.Equals(mode, "execution", StringComparison.Ordinal))
                        {
                            context.Request.Path = "/ag-ui/ticketingAgent";
                        }
                    }
                }
                catch (JsonException)
                {
                }
            }
        }

        await _next(context).ConfigureAwait(false);
    }
}
