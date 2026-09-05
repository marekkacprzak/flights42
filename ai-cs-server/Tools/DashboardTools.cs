using System.ComponentModel;
using System.Text.Json;
using AiCsServer.Dashboard;
using AiCsServer.Data;

namespace AiCsServer.Tools;

public static class DashboardTools
{
    public const string RenderDashboardToolName = "renderDashboard";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Description("""
        Render the Flight42 dashboard from a compact spec.
        Input is a { tiles: Tile[] } object describing which tiles to show.
        The server compiles this spec into a structured dashboard payload.
        AGUIStreamOptions MapResult maps the JsonElement result to a STATE_SNAPSHOT plus an a2ui-surface ACTIVITY_SNAPSHOT (no approval suspend).
        """)]
    public static async Task<JsonElement> RenderDashboard(
        [Description("Dashboard tiles array (or a full { tiles: [...] } object).")] JsonElement tiles)
    {
        DashboardSpec spec = ParseSpec(tiles);
        CompiledDashboard compiled = await CompileDashboard.CompileAsync(
            spec,
            catalogId: DashboardRequestAmbient.CurrentCatalogId).ConfigureAwait(false);

        string cacheKey = DashboardRequestAmbient.CurrentRequestHash;
        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            cacheKey = DashboardCacheStore.ComputeRequestHash(
                WrapAsUserMessage(JsonSerializer.Serialize(spec, JsonOptions)));
        }

        await DashboardCacheStore.WriteAsync(cacheKey, spec).ConfigureAwait(false);

        var payload = new
        {
            ok = true,
            surfaceId = compiled.SurfaceId,
            operations = compiled.Structural.Concat(compiled.DataModel).ToArray(),
            dataSteps = compiled.DataSteps,
        };
        return JsonSerializer.SerializeToElement(payload, JsonOptions);
    }

    private static DashboardSpec ParseSpec(JsonElement tiles)
    {
        if (tiles.ValueKind == JsonValueKind.Object && tiles.TryGetProperty("tiles", out _))
        {
            return JsonSerializer.Deserialize<DashboardSpec>(tiles.GetRawText(), JsonOptions)
                ?? throw new ArgumentException("Invalid dashboard spec.");
        }

        if (tiles.ValueKind == JsonValueKind.Array)
        {
            string wrapped = $"{{\"tiles\":{tiles.GetRawText()}}}";
            return JsonSerializer.Deserialize<DashboardSpec>(wrapped, JsonOptions)
                ?? throw new ArgumentException("Invalid dashboard tiles array.");
        }

        throw new ArgumentException("renderDashboard expects { tiles: [...] } or a tiles array.");
    }

    private static IEnumerable<JsonElement> WrapAsUserMessage(string text)
    {
        using JsonDocument doc = JsonDocument.Parse(
            $"{{\"role\":\"user\",\"content\":{JsonSerializer.Serialize(text)}}}");
        yield return doc.RootElement.Clone();
    }
}
