using System.ComponentModel;

namespace AiCsServer.Tools;

public static class WeatherForecastTool
{
    private static readonly string[] Conditions = ["Sunny", "Partly cloudy", "Cloudy", "Rain", "Thunder"];

    public sealed record WeatherForecast(string City, string Date, string Condition, int TemperatureC);

    private static int HashString(string value)
    {
        int hash = 0;
        foreach (char ch in value)
        {
            hash = (hash * 31 + ch);
        }

        return Math.Abs(hash);
    }

    public static WeatherForecast WeatherForecastCore(string city, string date)
    {
        string day = date.Length >= 10 ? date[..10] : date;
        int seed = HashString($"{city.ToLowerInvariant()}|{day}");
        return new WeatherForecast(city, day, Conditions[seed % Conditions.Length], (seed % 25) + 5 - 5);
    }

    [Description("""
        Returns a deterministic mocked weather forecast for a city on a specific date.
        Use it to enrich the booked-flights tile with a small forecast next to each booked flight.
        Date should be an ISO date string. Output: { city, date, condition, temperatureC }.
        """)]
    public static WeatherForecast GetWeatherForecast(
        [Description("City name, e.g. \"Hamburg\".")] string city,
        [Description("ISO date or date-time. Only the YYYY-MM-DD part is used.")] string date)
    {
        return WeatherForecastCore(city, date);
    }
}
