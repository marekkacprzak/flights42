using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiCsServer.Dashboard;

[JsonConverter(typeof(DashboardTileConverter))]
public abstract record DashboardTile(string Type);

public sealed record FlightsTableTile(string From, string To, int? MaxRows = null)
    : DashboardTile("flightsTable");

public sealed record DelayedFlightsTableTile(string From, string To, int? MaxRows = null)
    : DashboardTile("delayedFlightsTable");

public sealed record DelayShareChartTile(string From, string To, string? ChartType = null)
    : DashboardTile("delayShareChart");

public sealed record DelaysPerDayChartTile(string From, string To)
    : DashboardTile("delaysPerDayChart");

public sealed record BoardingPassesTile(int? Count = null)
    : DashboardTile("boardingPasses");

public sealed record BookedFlightsListTile(bool? ShowCheckInButton = null, bool? ShowWeather = null, int? MaxRows = null)
    : DashboardTile("bookedFlightsList");

public sealed record FlightSearchTile(string? DefaultFrom = null, string? DefaultTo = null)
    : DashboardTile("flightSearch");

public sealed record RentalCarsTile(string? City = null, int? MaxItems = null)
    : DashboardTile("rentalCars");

public sealed record HotelsTile(string? City = null, int? MaxItems = null)
    : DashboardTile("hotels");

public sealed record WeatherListTile(int? MaxRows = null)
    : DashboardTile("weatherList");

public sealed record DashboardSpec(IReadOnlyList<DashboardTile> Tiles);

public sealed class DashboardTileConverter : JsonConverter<DashboardTile>
{
    public override DashboardTile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("type", out JsonElement typeEl))
        {
            throw new JsonException("Dashboard tile missing type.");
        }

        string? type = typeEl.GetString();
        return type switch
        {
            "flightsTable" => new FlightsTableTile(
                root.GetProperty("from").GetString() ?? "",
                root.GetProperty("to").GetString() ?? "",
                root.TryGetProperty("maxRows", out JsonElement maxRows) && maxRows.ValueKind == JsonValueKind.Number
                    ? maxRows.GetInt32()
                    : null),
            "delayedFlightsTable" => new DelayedFlightsTableTile(
                root.GetProperty("from").GetString() ?? "",
                root.GetProperty("to").GetString() ?? "",
                root.TryGetProperty("maxRows", out JsonElement maxRows2) && maxRows2.ValueKind == JsonValueKind.Number
                    ? maxRows2.GetInt32()
                    : null),
            "delayShareChart" => new DelayShareChartTile(
                root.GetProperty("from").GetString() ?? "",
                root.GetProperty("to").GetString() ?? "",
                root.TryGetProperty("chartType", out JsonElement chartType) ? chartType.GetString() : null),
            "delaysPerDayChart" => new DelaysPerDayChartTile(
                root.GetProperty("from").GetString() ?? "",
                root.GetProperty("to").GetString() ?? ""),
            "boardingPasses" => new BoardingPassesTile(
                root.TryGetProperty("count", out JsonElement count) && count.ValueKind == JsonValueKind.Number
                    ? count.GetInt32()
                    : null),
            "bookedFlightsList" => new BookedFlightsListTile(
                root.TryGetProperty("showCheckInButton", out JsonElement checkIn) && checkIn.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? checkIn.GetBoolean()
                    : null,
                root.TryGetProperty("showWeather", out JsonElement weather) && weather.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? weather.GetBoolean()
                    : null,
                root.TryGetProperty("maxRows", out JsonElement maxRows3) && maxRows3.ValueKind == JsonValueKind.Number
                    ? maxRows3.GetInt32()
                    : null),
            "flightSearch" => new FlightSearchTile(
                root.TryGetProperty("defaultFrom", out JsonElement from) ? from.GetString() : null,
                root.TryGetProperty("defaultTo", out JsonElement to) ? to.GetString() : null),
            "rentalCars" => new RentalCarsTile(
                root.TryGetProperty("city", out JsonElement city) ? city.GetString() : null,
                root.TryGetProperty("maxItems", out JsonElement maxItems) && maxItems.ValueKind == JsonValueKind.Number
                    ? maxItems.GetInt32()
                    : null),
            "hotels" => new HotelsTile(
                root.TryGetProperty("city", out JsonElement hotelCity) ? hotelCity.GetString() : null,
                root.TryGetProperty("maxItems", out JsonElement hotelMax) && hotelMax.ValueKind == JsonValueKind.Number
                    ? hotelMax.GetInt32()
                    : null),
            "weatherList" => new WeatherListTile(
                root.TryGetProperty("maxRows", out JsonElement weatherMax) && weatherMax.ValueKind == JsonValueKind.Number
                    ? weatherMax.GetInt32()
                    : null),
            _ => throw new JsonException($"Unknown dashboard tile type: {type}"),
        };
    }

    public override void Write(Utf8JsonWriter writer, DashboardTile value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", value.Type);
        switch (value)
        {
            case FlightsTableTile tile:
                writer.WriteString("from", tile.From);
                writer.WriteString("to", tile.To);
                if (tile.MaxRows is int maxRows)
                {
                    writer.WriteNumber("maxRows", maxRows);
                }

                break;
            case DelayedFlightsTableTile tile:
                writer.WriteString("from", tile.From);
                writer.WriteString("to", tile.To);
                if (tile.MaxRows is int delayedMax)
                {
                    writer.WriteNumber("maxRows", delayedMax);
                }

                break;
            case DelayShareChartTile tile:
                writer.WriteString("from", tile.From);
                writer.WriteString("to", tile.To);
                if (!string.IsNullOrWhiteSpace(tile.ChartType))
                {
                    writer.WriteString("chartType", tile.ChartType);
                }

                break;
            case DelaysPerDayChartTile tile:
                writer.WriteString("from", tile.From);
                writer.WriteString("to", tile.To);
                break;
            case BoardingPassesTile tile:
                if (tile.Count is int count)
                {
                    writer.WriteNumber("count", count);
                }

                break;
            case BookedFlightsListTile tile:
                if (tile.ShowCheckInButton is bool checkIn)
                {
                    writer.WriteBoolean("showCheckInButton", checkIn);
                }

                if (tile.ShowWeather is bool weather)
                {
                    writer.WriteBoolean("showWeather", weather);
                }

                if (tile.MaxRows is int bookedMax)
                {
                    writer.WriteNumber("maxRows", bookedMax);
                }

                break;
            case FlightSearchTile tile:
                if (!string.IsNullOrWhiteSpace(tile.DefaultFrom))
                {
                    writer.WriteString("defaultFrom", tile.DefaultFrom);
                }

                if (!string.IsNullOrWhiteSpace(tile.DefaultTo))
                {
                    writer.WriteString("defaultTo", tile.DefaultTo);
                }

                break;
            case RentalCarsTile tile:
                if (!string.IsNullOrWhiteSpace(tile.City))
                {
                    writer.WriteString("city", tile.City);
                }

                if (tile.MaxItems is int carMax)
                {
                    writer.WriteNumber("maxItems", carMax);
                }

                break;
            case HotelsTile tile:
                if (!string.IsNullOrWhiteSpace(tile.City))
                {
                    writer.WriteString("city", tile.City);
                }

                if (tile.MaxItems is int hotelMax)
                {
                    writer.WriteNumber("maxItems", hotelMax);
                }

                break;
            case WeatherListTile tile:
                if (tile.MaxRows is int weatherMax)
                {
                    writer.WriteNumber("maxRows", weatherMax);
                }

                break;
        }

        writer.WriteEndObject();
    }
}
