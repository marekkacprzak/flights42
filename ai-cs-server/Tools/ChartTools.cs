using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using AiCsServer.Infrastructure;

namespace AiCsServer.Tools;

public static class ChartTools
{
    private static readonly string[] Palette =
    [
        "#3b82f6",
        "#f97316",
        "#10b981",
        "#a855f7",
        "#f59e0b",
        "#ef4444",
        "#0ea5e9",
        "#22c55e",
    ];

    private static readonly ConcurrentDictionary<string, string> ChartCache = new(StringComparer.Ordinal);
    private const int ChartCacheLimit = 200;
    private static int _chartCounter;

    public sealed record ChartDataset(
        [property: Description("Series label, shown in the legend when more than one series.")] string? Label,
        [property: Description("Numeric values for this series.")] double[] Data,
        [property: Description("Optional CSS color for this series.")] string? Color = null);

    public sealed record RenderChartResult(string Url);

    public static string? GetCachedChartSvg(string id)
    {
        return ChartCache.TryGetValue(id, out string? svg) ? svg : null;
    }

    public static string BuildAndCacheChartUrl(string type, string[] labels, ChartDataset[] datasets, string? title = null)
    {
        string svg = string.Equals(type, "pie", StringComparison.OrdinalIgnoreCase)
            ? RenderPieChart(labels, datasets, title)
            : RenderBarChart(labels, datasets, title);
        string id = RememberChart(svg);
        return PublicUrl.Combine($"/charts/{id}.svg");
    }

    private static string NextChartId()
    {
        int counter = Interlocked.Increment(ref _chartCounter) % 1_000_000;
        return $"c{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}{counter:x}";
    }

    private static string RememberChart(string svg)
    {
        string id = NextChartId();
        ChartCache[id] = svg;
        while (ChartCache.Count > ChartCacheLimit)
        {
            string? oldest = ChartCache.Keys.FirstOrDefault();
            if (oldest is null)
            {
                break;
            }

            ChartCache.TryRemove(oldest, out _);
        }

        return id;
    }

    [Description("""
        Render a small bar or pie chart as an SVG image.
        Returns { url } where url is a short HTTP URL pointing at the rendered SVG
        (e.g. http://localhost:3001/charts/<id>.svg). Embed that URL verbatim in an A2UI Image component.
        """)]
    public static RenderChartResult RenderChart(
        [Description("Chart type: bar or pie.")] string type,
        [Description("Category labels.")] string[] labels,
        [Description("One or more datasets.")] ChartDataset[] datasets,
        [Description("Optional chart title.")] string? title = null)
    {
        return new RenderChartResult(BuildAndCacheChartUrl(type, labels, datasets, title));
    }

    private static string RenderBarChart(string[] labels, ChartDataset[] datasets, string? title)
    {
        const int W = 640;
        const int H = 360;
        int padTop = string.IsNullOrWhiteSpace(title) ? 24 : 48;
        const int padRight = 16;
        int padBottom = datasets.Length > 1 ? 64 : 44;
        const int padLeft = 48;
        int innerW = W - padLeft - padRight;
        int innerH = H - padTop - padBottom;
        double max = Math.Max(1, datasets.SelectMany(d => d.Data).DefaultIfEmpty(0).Max());
        int groupCount = labels.Length;
        int seriesCount = datasets.Length;
        double groupWidth = innerW / (double)Math.Max(1, groupCount);
        double barWidth = (groupWidth * 0.7) / Math.Max(1, seriesCount);
        double groupPad = (groupWidth - barWidth * seriesCount) / 2;
        double[] yTicks = NiceTicks(max, 4);

        StringBuilder sb = new();
        sb.Append("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {W} {H}\" preserveAspectRatio=\"xMidYMid meet\">");
        sb.Append($"<rect width=\"{W}\" height=\"{H}\" fill=\"#ffffff\" />");
        if (!string.IsNullOrWhiteSpace(title))
        {
            sb.Append($"<text x=\"{W / 2}\" y=\"24\" text-anchor=\"middle\" font-family=\"system-ui, sans-serif\" font-size=\"16\" font-weight=\"600\" fill=\"#0f172a\">{EscapeXml(title)}</text>");
        }

        foreach (double tick in yTicks)
        {
            double y = padTop + innerH - (tick / max) * innerH;
            sb.Append($"<line x1=\"{padLeft}\" y1=\"{y.ToString(CultureInfo.InvariantCulture)}\" x2=\"{padLeft + innerW}\" y2=\"{y.ToString(CultureInfo.InvariantCulture)}\" stroke=\"#e2e8f0\" stroke-width=\"1\" />");
            sb.Append($"<text x=\"{padLeft - 6}\" y=\"{(y + 4).ToString(CultureInfo.InvariantCulture)}\" text-anchor=\"end\" font-family=\"system-ui, sans-serif\" font-size=\"11\" fill=\"#64748b\">{tick.ToString(CultureInfo.InvariantCulture)}</text>");
        }

        for (int gi = 0; gi < labels.Length; gi++)
        {
            double x = padLeft + gi * groupWidth + groupWidth / 2;
            sb.Append($"<text x=\"{x.ToString(CultureInfo.InvariantCulture)}\" y=\"{padTop + innerH + 18}\" text-anchor=\"middle\" font-family=\"system-ui, sans-serif\" font-size=\"11\" fill=\"#475569\">{EscapeXml(labels[gi])}</text>");
            for (int si = 0; si < datasets.Length; si++)
            {
                double value = gi < datasets[si].Data.Length ? datasets[si].Data[gi] : 0;
                double barH = (value / max) * innerH;
                double bx = padLeft + gi * groupWidth + groupPad + si * barWidth;
                double by = padTop + innerH - barH;
                string color = datasets[si].Color ?? Palette[si % Palette.Length];
                sb.Append($"<rect x=\"{bx.ToString(CultureInfo.InvariantCulture)}\" y=\"{by.ToString(CultureInfo.InvariantCulture)}\" width=\"{Math.Max(0, barWidth - 2).ToString(CultureInfo.InvariantCulture)}\" height=\"{barH.ToString(CultureInfo.InvariantCulture)}\" fill=\"{color}\" rx=\"2\" />");
            }
        }

        sb.Append($"<line x1=\"{padLeft}\" y1=\"{padTop + innerH}\" x2=\"{padLeft + innerW}\" y2=\"{padTop + innerH}\" stroke=\"#cbd5e1\" stroke-width=\"1\" />");
        if (seriesCount > 1)
        {
            for (int si = 0; si < datasets.Length; si++)
            {
                double x = padLeft + si * 120;
                double y = H - 16;
                string color = datasets[si].Color ?? Palette[si % Palette.Length];
                string label = datasets[si].Label ?? $"Series {si + 1}";
                sb.Append($"<rect x=\"{x}\" y=\"{y - 8}\" width=\"10\" height=\"10\" fill=\"{color}\" rx=\"2\" />");
                sb.Append($"<text x=\"{x + 14}\" y=\"{y}\" font-family=\"system-ui, sans-serif\" font-size=\"11\" fill=\"#475569\">{EscapeXml(label)}</text>");
            }
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string RenderPieChart(string[] labels, ChartDataset[] datasets, string? title)
    {
        double[] data = datasets.Length > 0 ? datasets[0].Data : [];
        const int W = 480;
        const int H = 320;
        const int Cx = 320;
        int cy = string.IsNullOrWhiteSpace(title) ? 156 : 168;
        const int R = 110;
        double total = data.Sum();
        if (total <= 0)
        {
            total = 1;
        }

        StringBuilder sb = new();
        sb.Append("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {W} {H}\" preserveAspectRatio=\"xMidYMid meet\">");
        sb.Append($"<rect width=\"{W}\" height=\"{H}\" fill=\"#ffffff\" />");
        if (!string.IsNullOrWhiteSpace(title))
        {
            sb.Append($"<text x=\"{W / 2}\" y=\"24\" text-anchor=\"middle\" font-family=\"system-ui, sans-serif\" font-size=\"16\" font-weight=\"600\" fill=\"#0f172a\">{EscapeXml(title)}</text>");
        }

        double angle = -Math.PI / 2;
        for (int i = 0; i < data.Length; i++)
        {
            double value = data[i];
            double sweep = (value / total) * Math.PI * 2;
            double startAngle = angle;
            double endAngle = startAngle + sweep;
            angle = endAngle;
            string color = Palette[i % Palette.Length];
            if (sweep == 0)
            {
                continue;
            }

            if (sweep >= Math.PI * 2 - 1e-6)
            {
                sb.Append($"<circle cx=\"{Cx}\" cy=\"{cy}\" r=\"{R}\" fill=\"{color}\" />");
                continue;
            }

            double x1 = Cx + R * Math.Cos(startAngle);
            double y1 = cy + R * Math.Sin(startAngle);
            double x2 = Cx + R * Math.Cos(endAngle);
            double y2 = cy + R * Math.Sin(endAngle);
            int largeArc = sweep > Math.PI ? 1 : 0;
            sb.Append($"<path d=\"M {Cx} {cy} L {x1.ToString("0.##", CultureInfo.InvariantCulture)} {y1.ToString("0.##", CultureInfo.InvariantCulture)} A {R} {R} 0 {largeArc} 1 {x2.ToString("0.##", CultureInfo.InvariantCulture)} {y2.ToString("0.##", CultureInfo.InvariantCulture)} Z\" fill=\"{color}\" />");
        }

        for (int i = 0; i < labels.Length; i++)
        {
            double value = i < data.Length ? data[i] : 0;
            int pct = total > 0 ? (int)Math.Round((value / total) * 100) : 0;
            string color = Palette[i % Palette.Length];
            int x = 24;
            int y = (string.IsNullOrWhiteSpace(title) ? 40 : 56) + i * 20;
            sb.Append($"<rect x=\"{x}\" y=\"{y - 9}\" width=\"10\" height=\"10\" fill=\"{color}\" rx=\"2\" />");
            sb.Append($"<text x=\"{x + 14}\" y=\"{y}\" font-family=\"system-ui, sans-serif\" font-size=\"11\" fill=\"#475569\">{EscapeXml(labels[i])} — {value.ToString(CultureInfo.InvariantCulture)} ({pct}%)</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static double[] NiceTicks(double max, int count)
    {
        if (max <= 0)
        {
            return [0];
        }

        double step = NiceStep(max / Math.Max(1, count));
        List<double> ticks = new();
        for (double v = 0; v <= max + step / 2; v += step)
        {
            ticks.Add(Math.Round(v * 100) / 100);
        }

        return ticks.ToArray();
    }

    private static double NiceStep(double rough)
    {
        if (rough <= 0)
        {
            return 1;
        }

        double exp = Math.Floor(Math.Log10(rough));
        double f = rough / Math.Pow(10, exp);
        double nice;
        if (f < 1.5)
        {
            nice = 1;
        }
        else if (f < 3)
        {
            nice = 2;
        }
        else if (f < 7)
        {
            nice = 5;
        }
        else
        {
            nice = 10;
        }

        return nice * Math.Pow(10, exp);
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }
}
