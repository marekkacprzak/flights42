using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;

namespace AiCsServer.Data;

public sealed record PlanFlight(
    [property: Description("The flight id")] int Id,
    [property: Description("Departure city (city name, no code)")] string From,
    [property: Description("Arrival city (city name, no code)")] string To,
    [property: Description("Departure date in ISO format")] string Date,
    [property: Description("Delay in minutes (0 if on time)")] int Delay);

public sealed record PlanHotel(
    [property: Description("Stable hotel id (e.g. \"grand-palace\")")] string Id,
    [property: Description("Full hotel name including the city")] string Name,
    [property: Description("Star rating from 1 to 5")] int Stars,
    [property: Description("Absolute or app-relative URL to a hotel image")] string ImageUrl,
    [property: Description("City the hotel is located in")] string City);

public sealed record TravelPlan(string Summary, IReadOnlyList<PlanFlight> Flights, IReadOnlyList<PlanHotel> Hotels);

public static class TravelPlanStore
{
    private static readonly ConcurrentDictionary<string, TravelPlan> Plans = new(StringComparer.Ordinal);
    private static readonly TravelPlan Empty = new("", Array.Empty<PlanFlight>(), Array.Empty<PlanHotel>());
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static TravelPlan Read(string? sessionKey = null)
    {
        string key = string.IsNullOrWhiteSpace(sessionKey) ? "default" : sessionKey;
        if (Plans.TryGetValue(key, out TravelPlan? plan))
        {
            return plan;
        }

        TravelPlan? loaded = LoadFromDb(key);
        if (loaded is not null)
        {
            Plans[key] = loaded;
            return loaded;
        }

        return Empty;
    }

    public static TravelPlan Commit(TravelPlan plan, string? sessionKey = null)
    {
        string key = string.IsNullOrWhiteSpace(sessionKey) ? "default" : sessionKey;
        TravelPlan ordered = plan with { Hotels = OrderHotelsByRoute(plan.Hotels, plan.Flights) };
        Plans[key] = ordered;
        Persist(key, ordered);
        return ordered;
    }

    private static TravelPlan? LoadFromDb(string key)
    {
        IReadOnlyList<object[]> rows = LibSqlDatabase.QueryAsync(
            "SELECT summary, payload_json FROM travel_plans WHERE session_key = ?", key)
            .GetAwaiter().GetResult();
        if (rows.Count == 0)
        {
            return null;
        }

        string summary = Convert.ToString(rows[0][0]) ?? "";
        string json = Convert.ToString(rows[0][1]) ?? "";
        StoredPlan? stored = JsonSerializer.Deserialize<StoredPlan>(json, JsonOptions);
        if (stored is null)
        {
            return null;
        }

        return new TravelPlan(summary, stored.Flights ?? [], stored.Hotels ?? []);
    }

    private static void Persist(string key, TravelPlan plan)
    {
        string payload = JsonSerializer.Serialize(new StoredPlan(plan.Flights.ToArray(), plan.Hotels.ToArray()), JsonOptions);
        LibSqlDatabase.ExecuteAsync(
            """
            INSERT INTO travel_plans (session_key, summary, payload_json)
            VALUES (?, ?, ?)
            ON CONFLICT(session_key) DO UPDATE SET
              summary = excluded.summary,
              payload_json = excluded.payload_json
            """,
            key,
            plan.Summary,
            payload).GetAwaiter().GetResult();
    }

    private sealed record StoredPlan(PlanFlight[] Flights, PlanHotel[] Hotels);

    private static IReadOnlyList<PlanHotel> OrderHotelsByRoute(
        IReadOnlyList<PlanHotel> hotels,
        IReadOnlyList<PlanFlight> flights)
    {
        Dictionary<string, int> arrivalOrder = new(StringComparer.Ordinal);
        for (int index = 0; index < flights.Count; index++)
        {
            PlanFlight flight = flights[index];
            if (!arrivalOrder.ContainsKey(flight.To))
            {
                arrivalOrder[flight.To] = index;
            }
        }

        return hotels
            .OrderBy(hotel => arrivalOrder.TryGetValue(hotel.City, out int rank) ? rank : int.MaxValue)
            .ToArray();
    }
}
