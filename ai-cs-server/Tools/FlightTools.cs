using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using AiCsServer.Data;

namespace AiCsServer.Tools;

public static class FlightTools
{
    private const string FlightApiBase = "https://demo.angulararchitects.io/api/flight";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { Accept = { new MediaTypeWithQualityHeaderValue("application/json") } },
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

    public sealed record FlightRecord(int Id, string From, string To, string Date, int Delay);

    public sealed record FindBookedFlightsResult(IReadOnlyList<BookedFlight> Flights);

    public sealed record SearchFlightsResult(IReadOnlyList<FlightRecord> Flights);

    public sealed record BookFlightSuccess(bool Ok, string Result, BookedFlight Flight, string? PaymentMethod);

    public sealed record BookFlightFailure(bool Ok, string Result, string Code);

    private static int NormaliseDelay(RawFlight raw)
    {
        if (raw.Delay is int delay)
        {
            return delay;
        }

        return raw.Delayed == true ? 15 : 0;
    }

    public static async Task<IReadOnlyList<FlightRecord>> FetchFlightsAsync(string from, string to, CancellationToken cancellationToken = default)
    {
        string url = $"{FlightApiBase}?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";
        using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        List<RawFlight>? raw = await response.Content.ReadFromJsonAsync<List<RawFlight>>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (raw is null)
        {
            return Array.Empty<FlightRecord>();
        }

        return raw.Select(entry => new FlightRecord(
            entry.Id,
            CityAliases.CanonicalCity(entry.From),
            CityAliases.CanonicalCity(entry.To),
            entry.Date,
            NormaliseDelay(entry))).ToArray();
    }

    public static async Task<IReadOnlyList<FlightRecord>> SearchFlightsAsync(
        string from,
        string to,
        string? date = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FlightRecord> flights = Array.Empty<FlightRecord>();
        foreach (string fromCity in CityAliases.CityCandidates(from))
        {
            foreach (string toCity in CityAliases.CityCandidates(to))
            {
                flights = await FetchFlightsAsync(fromCity, toCity, cancellationToken).ConfigureAwait(false);
                if (flights.Count > 0)
                {
                    break;
                }
            }

            if (flights.Count > 0)
            {
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(date))
        {
            return flights;
        }

        string day = FormatDate.ToDateOnly(date);
        return flights.Where(flight => FormatDate.ToDateOnly(flight.Date) == day).ToArray();
    }

    [Description("Returns the flights that are already booked by the current passenger.")]
    public static async Task<FindBookedFlightsResult> FindBookedFlights()
    {
        return new FindBookedFlightsResult(await BookedFlightsStore.GetBookedFlightsAsync().ConfigureAwait(false));
    }

    [Description("""
        Searches public flight data between two cities and returns the matching flights.
        For the search parameters, use city names with the first letter in upper case (e.g. "Graz", "Hamburg"). NEVER use airport codes.
        When the user mentions a specific day, filter the result locally by ISO date prefix (YYYY-MM-DD) on the returned flights.
        """)]
    public static async Task<SearchFlightsResult> SearchFlights(
        [Description("Departure city name, e.g. \"Graz\"")] string from,
        [Description("Destination city name, e.g. \"Hamburg\"")] string to,
        [Description("Optional ISO date without time, e.g. \"2026-06-23\".")] string? date = null)
    {
        return new SearchFlightsResult(await SearchFlightsAsync(from, to, date).ConfigureAwait(false));
    }

    [Description("Books a flight for the current passenger. When approval is enabled the user chooses credit card or bonus miles; otherwise credit card is used. Fails if the flight does not exist or is already booked.")]
    public static async Task<object> BookFlight(
        [Description("The id of the flight to book.")] int flightId)
    {
        return await CompleteBookFlightAsync(flightId, "creditCard").ConfigureAwait(false);
    }

    [Description("Cancels a previously booked flight for the current passenger. Fails if the flight is not booked.")]
    public static async Task<object> CancelFlight(
        [Description("The id of the flight to cancel.")] int flightId)
    {
        return await CompleteCancelFlightAsync(flightId, approved: true).ConfigureAwait(false);
    }

    public static async Task<object> CompleteBookFlightAsync(int flightId, string selection)
    {
        if (selection == "cancel")
        {
            return new BookFlightFailure(false, $"Booking of flight {flightId} was cancelled by the user.", "USER_CANCELLED");
        }

        if (BookedFlightsStore.IsBooked(flightId))
        {
            return new BookFlightFailure(false, $"Flight {flightId} is already booked.", "ALREADY_BOOKED");
        }

        BookedFlight? flight = await BookedFlightsStore.FetchFlightAsync(flightId).ConfigureAwait(false);
        if (flight is null)
        {
            return new BookFlightFailure(false, $"Flight {flightId} does not exist.", "NOT_FOUND");
        }

        string paymentMethod = selection == "miles" ? "miles" : "creditCard";
        string paymentSuffix = paymentMethod == "creditCard" ? "credit card" : "bonus miles";

        BookedFlightsStore.AddBooking(flightId);
        return new BookFlightSuccess(
            true,
            $"Booked flight {flightId} from {flight.From} to {flight.To} on {FormatDate.FormatFlightDate(flight.Date)} (paid with {paymentSuffix}).",
            flight,
            paymentMethod);
    }

    public static async Task<object> CompleteCancelFlightAsync(int flightId, bool approved)
    {
        if (!approved)
        {
            return new BookFlightFailure(false, $"Cancellation of flight {flightId} was cancelled by the user.", "USER_CANCELLED");
        }

        if (!BookedFlightsStore.IsBooked(flightId))
        {
            return new BookFlightFailure(false, $"Flight {flightId} is not booked.", "NOT_BOOKED");
        }

        BookedFlight? flight = null;
        try
        {
            flight = await BookedFlightsStore.FetchFlightAsync(flightId).ConfigureAwait(false);
        }
        catch
        {
        }

        BookedFlightsStore.RemoveBooking(flightId);

        if (flight is null)
        {
            return new BookFlightFailure(false, $"Flight {flightId} could not be loaded after cancellation.", "NOT_FOUND");
        }

        return new BookFlightSuccess(
            true,
            $"Cancelled flight {flightId} from {flight.From} to {flight.To} on {FormatDate.FormatFlightDate(flight.Date)}.",
            flight,
            null);
    }
}
