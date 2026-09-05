using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Serialization;

namespace AiCsServer.Data;

public sealed record BookedFlight(int Id, string From, string To, string Date, int Delay);

public static class BookedFlightsStore
{
    private const string FlightApiBase = "https://demo.angulararchitects.io/api/flight";

    private static readonly ConcurrentDictionary<int, byte> BookedFlightIds = new();
    private static readonly ConcurrentDictionary<int, BookedFlight> FlightCache = new();
    private static readonly object SeedGate = new();
    private static bool _seeded;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private sealed class RawFlight
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("from")]
        public string From { get; set; } = "";

        [JsonPropertyName("to")]
        public string To { get; set; } = "";

        [JsonPropertyName("date")]
        public string Date { get; set; } = "";

        [JsonPropertyName("delayed")]
        public bool? Delayed { get; set; }

        [JsonPropertyName("delay")]
        public int? Delay { get; set; }
    }

    public static void EnsureLoaded()
    {
        lock (SeedGate)
        {
            if (_seeded)
            {
                return;
            }

            IReadOnlyList<object[]> rows = LibSqlDatabase.QueryAsync(
                "SELECT flight_id FROM booked_flights ORDER BY flight_id").GetAwaiter().GetResult();

            if (rows.Count == 0)
            {
                foreach (int id in new[] { 1, 2, 50, 516 })
                {
                    LibSqlDatabase.ExecuteAsync(
                        "INSERT OR IGNORE INTO booked_flights (flight_id) VALUES (?)", id)
                        .GetAwaiter().GetResult();
                    BookedFlightIds[id] = 0;
                }
            }
            else
            {
                foreach (object[] row in rows)
                {
                    int id = Convert.ToInt32(row[0]);
                    BookedFlightIds[id] = 0;
                }
            }

            _seeded = true;
        }
    }

    public static async Task<BookedFlight?> FetchFlightAsync(int id, CancellationToken cancellationToken = default)
    {
        if (FlightCache.TryGetValue(id, out BookedFlight? cached))
        {
            return cached;
        }

        using HttpResponseMessage response = await Http.GetAsync($"{FlightApiBase}/{id}", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        RawFlight? raw = await response.Content.ReadFromJsonAsync<RawFlight>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (raw is null)
        {
            return null;
        }

        BookedFlight flight = new(
            raw.Id,
            raw.From,
            raw.To,
            raw.Date,
            raw.Delayed == true ? (raw.Delay ?? 0) : 0);

        FlightCache[id] = flight;
        return flight;
    }

    public static bool IsBooked(int flightId)
    {
        EnsureLoaded();
        return BookedFlightIds.ContainsKey(flightId);
    }

    public static void AddBooking(int flightId)
    {
        EnsureLoaded();
        BookedFlightIds[flightId] = 0;
        LibSqlDatabase.ExecuteAsync(
            "INSERT OR IGNORE INTO booked_flights (flight_id) VALUES (?)", flightId)
            .GetAwaiter().GetResult();
    }

    public static void RemoveBooking(int flightId)
    {
        EnsureLoaded();
        BookedFlightIds.TryRemove(flightId, out _);
        LibSqlDatabase.ExecuteAsync(
            "DELETE FROM booked_flights WHERE flight_id = ?", flightId)
            .GetAwaiter().GetResult();
    }

    public static IReadOnlyList<int> GetBookedFlightIds()
    {
        EnsureLoaded();
        return BookedFlightIds.Keys.OrderBy(id => id).ToArray();
    }

    public static async Task<IReadOnlyList<BookedFlight>> GetBookedFlightsAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureLoaded();
        List<BookedFlight> flights = new();
        foreach (int id in GetBookedFlightIds())
        {
            BookedFlight? flight = await FetchFlightAsync(id, cancellationToken).ConfigureAwait(false);
            if (flight is not null)
            {
                flights.Add(flight);
            }
        }

        return flights;
    }
}
