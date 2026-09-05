using AiCsServer.Tools;

namespace AiCsServer.Workflows;

public static class PackageTourUtils
{
    public static IReadOnlyList<string> OvernightCitiesFromLegs(IReadOnlyList<WorkflowLeg> legs)
    {
        List<string> cities = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < legs.Count - 1; i++)
        {
            string arrivalDay = FormatDate.ToDateOnly(legs[i].Date);
            string nextDepartureDay = FormatDate.ToDateOnly(legs[i + 1].Date);
            if (string.CompareOrdinal(nextDepartureDay, arrivalDay) <= 0)
            {
                continue;
            }

            string city = CityAliases.CanonicalCity(legs[i].To);
            if (!seen.Add(city))
            {
                continue;
            }

            cities.Add(city);
        }

        return cities;
    }

    public static FinalPlan CreatePlan(
        FinalPlanDraft raw,
        IReadOnlyList<WorkflowLeg> legs,
        IReadOnlyList<WorkflowDestination> destinations)
    {
        List<FlightTools.FlightRecord> chosenFlights = new();
        foreach (WorkflowLeg leg in legs.Where(entry => entry.Candidates.Count > 0))
        {
            FlightTools.FlightRecord? chosen = raw.Flights?.FirstOrDefault(flight =>
                leg.Candidates.Any(candidate => candidate.Id == flight.Id));
            chosenFlights.Add(chosen ?? leg.Candidates[0]);
        }

        List<HotelTools.FindHotel> chosenHotels = new();
        foreach (WorkflowDestination destination in destinations)
        {
            HotelTools.FindHotel? chosen = destination.Hotels.FirstOrDefault(hotel =>
                raw.Hotels?.Any(entry => entry.City == hotel.City && entry.Id == hotel.Id) == true);
            if (chosen is null && destination.Hotels.Count > 0)
            {
                chosen = destination.Hotels[0];
            }

            if (chosen is not null)
            {
                chosenHotels.Add(chosen);
            }
        }

        return new FinalPlan(raw.Summary ?? "", chosenFlights, chosenHotels);
    }
}
