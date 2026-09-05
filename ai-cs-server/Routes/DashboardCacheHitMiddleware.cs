using System.Text.Json;
using AiCsServer.Dashboard;
using AiCsServer.Data;

namespace AiCsServer.Routes;

public sealed class DashboardCacheHitMiddleware
{
    private readonly RequestDelegate _next;

    public DashboardCacheHitMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method) ||
            !context.Request.Path.Equals("/ag-ui/dashboardAgent", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Request.EnableBuffering();

        try
        {
            using JsonDocument body = await JsonDocument.ParseAsync(context.Request.Body).ConfigureAwait(false);
            context.Request.Body.Position = 0;

            DashboardRequestAmbient.CurrentCatalogId = ExtractCatalogId(body.RootElement);
            if (body.RootElement.TryGetProperty("messages", out JsonElement messagesEl) &&
                messagesEl.ValueKind == JsonValueKind.Array)
            {
                DashboardRequestAmbient.CurrentRequestHash =
                    DashboardCacheStore.ComputeRequestHash(messagesEl.EnumerateArray());
            }

            bool served = await DashboardAgUiEndpoints.TryWriteCachedDashboardSseAsync(context, body.RootElement)
                .ConfigureAwait(false);
            if (served)
            {
                return;
            }
        }
        catch (JsonException)
        {
            context.Request.Body.Position = 0;
        }

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            DashboardRequestAmbient.CurrentCatalogId = null;
            DashboardRequestAmbient.CurrentRequestHash = null;
        }
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
            catch (JsonException)
            {
            }
        }

        return null;
    }
}

public static class DashboardCacheHitMiddlewareExtensions
{
    public static IApplicationBuilder UseDashboardCacheHit(this IApplicationBuilder app)
    {
        return app.UseMiddleware<DashboardCacheHitMiddleware>();
    }
}
