using System.ComponentModel;
using System.Text.Json.Serialization;
using AiCsServer.Tools;

namespace AiCsServer.Workflows;

public sealed record RoughPlanFlight(
    [property: Description("Departure city (name, not IATA code).")] string From,
    [property: Description("Destination city (name, not IATA code).")] string To,
    [property: Description("Departure day as ISO date without time, e.g. \"2026-06-23\".")] string Date);

public sealed record RoughPlanHotel(
    [property: Description("City the traveller wants to stay in.")] string City);

public sealed record WorkflowLeg(
    string From,
    string To,
    string Date,
    IReadOnlyList<FlightTools.FlightRecord> Candidates);

public sealed record WorkflowDestination(
    string City,
    IReadOnlyList<HotelTools.FindHotel> Hotels);

public sealed record FinalPlanDraft(
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("flights")] List<FlightTools.FlightRecord>? Flights,
    [property: JsonPropertyName("hotels")] List<HotelTools.FindHotel>? Hotels);

public sealed record FinalPlan(
    string Summary,
    IReadOnlyList<FlightTools.FlightRecord> Flights,
    IReadOnlyList<HotelTools.FindHotel> Hotels);
