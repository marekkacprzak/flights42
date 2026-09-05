using System.ComponentModel;

namespace AiCsDemo.Server;

public static class WeatherTool
{
    [Description("Returns the current weather and temperature for a given city.")]
    public static Weather GetWeather(
        [Description("The name of the city.")] string city)
    {
        return WeatherApi.GetWeather(city);
    }
}
