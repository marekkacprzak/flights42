using System.ComponentModel;

namespace AiCsServer.Tools;

public static class FlightChartTools
{
    [Description("""
        One-shot helper for delay-related chart tiles. Combines searchFlights + aggregation + renderChart.
        Modes: delayShare (on-time vs delayed), delaysPerDay (grouped bars per day).
        Returns { url, stats }.
        """)]
    public static async Task<object> RenderFlightChart(
        [Description("Departure city, e.g. \"Graz\".")] string from,
        [Description("Destination city, e.g. \"Hamburg\".")] string to,
        [Description("Aggregation mode: delayShare or delaysPerDay.")] string type,
        [Description("Chart type: bar or pie.")] string chartType,
        [Description("Optional ISO date prefix (YYYY-MM-DD).")] string? date = null,
        [Description("Optional chart title.")] string? title = null)
    {
        IReadOnlyList<FlightTools.FlightRecord> all = await FlightTools.FetchFlightsAsync(from, to).ConfigureAwait(false);
        IReadOnlyList<FlightTools.FlightRecord> flights = string.IsNullOrWhiteSpace(date)
            ? all
            : all.Where(f => f.Date.StartsWith(date, StringComparison.Ordinal)).ToArray();

        if (string.Equals(type, "delayShare", StringComparison.OrdinalIgnoreCase))
        {
            int onTime = flights.Count(f => f.Delay <= 0);
            int delayed = flights.Count(f => f.Delay > 0);
            string url = ChartTools.BuildAndCacheChartUrl(
                chartType,
                ["On time", "Delayed"],
                [new ChartTools.ChartDataset("Flights", [onTime, delayed])],
                title);
            return new
            {
                url,
                stats = new { mode = "delayShare", total = flights.Count, onTime, delayed },
            };
        }

        Dictionary<string, (int OnTime, int Delayed)> buckets = new(StringComparer.Ordinal);
        foreach (FlightTools.FlightRecord flight in flights)
        {
            string day = flight.Date.Length >= 10 ? flight.Date[..10] : flight.Date;
            (int OnTime, int Delayed) bucket = buckets.GetValueOrDefault(day);
            if (flight.Delay > 0)
            {
                bucket.Delayed += 1;
            }
            else
            {
                bucket.OnTime += 1;
            }

            buckets[day] = bucket;
        }

        KeyValuePair<string, (int OnTime, int Delayed)>[] sorted = buckets.OrderBy(entry => entry.Key).ToArray();
        string[] labels = sorted.Select(entry => entry.Key).ToArray();
        ChartTools.ChartDataset[] datasets = string.Equals(chartType, "pie", StringComparison.OrdinalIgnoreCase)
            ? [new ChartTools.ChartDataset("Delayed", sorted.Select(entry => (double)entry.Value.Delayed).ToArray())]
            :
            [
                new ChartTools.ChartDataset("On time", sorted.Select(entry => (double)entry.Value.OnTime).ToArray()),
                new ChartTools.ChartDataset("Delayed", sorted.Select(entry => (double)entry.Value.Delayed).ToArray()),
            ];
        string chartUrl = ChartTools.BuildAndCacheChartUrl(chartType, labels, datasets, title);
        return new
        {
            url = chartUrl,
            stats = new
            {
                mode = "delaysPerDay",
                dates = sorted.Select(entry => new { date = entry.Key, onTime = entry.Value.OnTime, delayed = entry.Value.Delayed }).ToArray(),
            },
        };
    }
}
