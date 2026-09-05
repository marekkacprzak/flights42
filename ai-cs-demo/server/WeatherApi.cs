namespace AiCsDemo.Server;

public sealed record Weather(string City, string Condition, int Temperature);

public static class WeatherApi
{
    private static readonly string[] Conditions = ["rainy", "sunny", "cloudy"];
    private static readonly Dictionary<string, Weather> WeatherCache = new(StringComparer.Ordinal);
    private static readonly Lock CacheLock = new();

    public static Weather GetWeather(string city)
    {
        var normalized = city.Trim().ToLowerInvariant();

        lock (CacheLock)
        {
            if (WeatherCache.TryGetValue(normalized, out var cached))
            {
                return cached;
            }

            var weather = new Weather(
                city,
                Conditions[Random.Shared.Next(Conditions.Length)],
                Random.Shared.Next(10, 26));
            WeatherCache[normalized] = weather;
            return weather;
        }
    }
}
