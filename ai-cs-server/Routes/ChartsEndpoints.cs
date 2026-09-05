using AiCsServer.Tools;

namespace AiCsServer.Routes;

public static class ChartsEndpoints
{
    public static void MapCharts(this WebApplication app)
    {
        app.MapGet("/charts/{id}", (string id) =>
        {
            string chartId = id.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                ? id[..^4]
                : id;
            string? svg = ChartTools.GetCachedChartSvg(chartId);
            if (svg is null)
            {
                return Results.Text("chart not found", statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Content(svg, "image/svg+xml; charset=utf-8");
        });
    }
}
