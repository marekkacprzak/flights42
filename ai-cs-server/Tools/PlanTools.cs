using System.ComponentModel;
using AiCsServer.Data;

namespace AiCsServer.Tools;

public static class PlanTools
{
    [Description("""
        Returns the current travel plan: { summary, flights, hotels }. Each flight has
        id, from, to, date (ISO) and delay; each hotel has id, name, stars, city.
        The current plan is also provided to you as data above the conversation — use
        this tool mainly to VERIFY the plan after you changed it.
        """)]
    public static TravelPlan GetTravelPlan()
    {
        return TravelPlanStore.Read();
    }

    [Description("""
        Replaces the ENTIRE travel plan with a new, fully consistent one (flights in
        travel order + one hotel per overnight city). Use this whenever a change affects
        more than one item or the city sequence — e.g. changing a flight leg that also
        changes the destination city (a hotel must be swapped) or the connecting/return
        flight. Build the complete new plan first (search any new flights/hotels), then
        commit it here in one go. Only call on explicit user request.
        """)]
    public static object SetTravelPlan(
        [Description("The complete list of flights, in travel order.")] PlanFlight[] flights,
        [Description("The complete list of hotels, one per overnight city.")] PlanHotel[] hotels,
        [Description("Optional new summary. If omitted, the current summary is kept.")] string? summary = null)
    {
        TravelPlan current = TravelPlanStore.Read();
        TravelPlanStore.Commit(new TravelPlan(summary ?? current.Summary, flights, hotels));
        return new { flights = flights.Length, hotels = hotels.Length };
    }

    [Description("Adds a flight to the current travel plan. Only call this when the user explicitly asks to add a flight to the plan.")]
    public static object AddFlightToPlan(
        [Description("The flight to add to the plan")] PlanFlight flight)
    {
        TravelPlan plan = TravelPlanStore.Read();
        bool exists = plan.Flights.Any(existing => existing.Id == flight.Id);
        IReadOnlyList<PlanFlight> flights = exists
            ? plan.Flights.Select(existing => existing.Id == flight.Id ? flight : existing).ToArray()
            : [.. plan.Flights, flight];
        TravelPlanStore.Commit(plan with { Flights = flights });
        return new { added = flight.Id };
    }

    [Description("Removes a flight from the current travel plan by its id. Only call this when the user explicitly asks to remove a flight.")]
    public static object RemoveFlightFromPlan(
        [Description("Id of the flight to remove from the plan")] int flightId)
    {
        TravelPlan plan = TravelPlanStore.Read();
        TravelPlanStore.Commit(plan with
        {
            Flights = plan.Flights.Where(flight => flight.Id != flightId).ToArray(),
        });
        return new { removed = flightId };
    }

    [Description("Replaces a flight in the current travel plan with another one. Use this when the user wants a different flight instead of the current one on a route. Only call on explicit request.")]
    public static object ReplaceFlightInPlan(
        [Description("Id of the flight currently in the plan that should be replaced")] int oldFlightId,
        [Description("The new flight to use instead")] PlanFlight flight)
    {
        TravelPlan plan = TravelPlanStore.Read();
        TravelPlanStore.Commit(plan with
        {
            Flights = plan.Flights.Select(existing => existing.Id == oldFlightId ? flight : existing).ToArray(),
        });
        return new { replaced = oldFlightId, with = flight.Id };
    }

    [Description("Adds a hotel to the current travel plan, or replaces the existing hotel for the same city (the plan holds at most one hotel per city). Use this both for adding a hotel and for swapping a city’s hotel for a different one. Only call when the user explicitly asks for it.")]
    public static object AddHotelToPlan(
        [Description("The hotel to add to the plan")] PlanHotel hotel)
    {
        TravelPlan plan = TravelPlanStore.Read();
        PlanHotel[] hotels =
        [
            .. plan.Hotels.Where(existing => existing.City != hotel.City),
            hotel,
        ];
        TravelPlanStore.Commit(plan with { Hotels = hotels });
        return new { added = hotel.Id };
    }

    [Description("Removes a hotel from the current travel plan by its id. Only call this when the user explicitly asks to remove a hotel.")]
    public static object RemoveHotelFromPlan(
        [Description("Id of the hotel to remove from the plan")] string hotelId)
    {
        TravelPlan plan = TravelPlanStore.Read();
        TravelPlanStore.Commit(plan with
        {
            Hotels = plan.Hotels.Where(hotel => hotel.Id != hotelId).ToArray(),
        });
        return new { removed = hotelId };
    }
}
