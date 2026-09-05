using System.ComponentModel;
using System.Text.Json;
using AiCsServer.Prompts;
using AiCsServer.Tools;
using Microsoft.Extensions.AI;

namespace AiCsServer.Workflows;

public static class PackageTourWorkflow
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IChatClient? ChatClient { get; set; }

    [Description("""
        Takes a rough plan, loads flights and hotels for it deterministically, then lets an agent build the final travel plan.
        Input: userPrompt + flights legs with from/to/date (and optional hotel cities).
        """)]
    public static async Task<object> RunAsync(
        [Description("The original user request, verbatim.")] string userPrompt,
        [Description("Flight legs in travel order.")] RoughPlanFlight[] flights,
        [Description("Optional cities the traveller wants to stay in.")] RoughPlanHotel[]? hotels = null)
    {
        _ = hotels;
        if (flights is null || flights.Length == 0)
        {
            return new { ok = false, message = "packageTourWorkflow requires at least one flight leg." };
        }

        List<WorkflowLeg> legs = new();
        foreach (RoughPlanFlight leg in flights)
        {
            IReadOnlyList<FlightTools.FlightRecord> candidates = await FlightTools.SearchFlightsAsync(
                leg.From, leg.To, leg.Date).ConfigureAwait(false);
            legs.Add(new WorkflowLeg(leg.From, leg.To, leg.Date, candidates));
        }

        IReadOnlyList<string> overnightCities = PackageTourUtils.OvernightCitiesFromLegs(legs);
        List<WorkflowDestination> destinations = overnightCities
            .Select(city => new WorkflowDestination(city, HotelTools.FindHotelsForCity(city)))
            .ToList();

        FinalPlanDraft draft = await FinalizeWithAgentAsync(userPrompt, legs, destinations).ConfigureAwait(false);
        FinalPlan plan = PackageTourUtils.CreatePlan(draft, legs, destinations);

        return new
        {
            ok = true,
            summary = plan.Summary,
            flights = plan.Flights,
            hotels = plan.Hotels,
            steps = new
            {
                findFlights = new { legCount = legs.Count },
                findHotels = new { cityCount = destinations.Count },
                finalize = new { ok = true },
            },
        };
    }

    private static async Task<FinalPlanDraft> FinalizeWithAgentAsync(
        string userPrompt,
        IReadOnlyList<WorkflowLeg> legs,
        IReadOnlyList<WorkflowDestination> destinations)
    {
        if (ChatClient is null)
        {
            return HeuristicFinalize(userPrompt, legs, destinations);
        }

        string prompt = $"""
            Original user request: {userPrompt}

            Available flights per leg (in travel order):
            {JsonSerializer.Serialize(legs, JsonOptions)}

            Available hotels per city:
            {JsonSerializer.Serialize(destinations, JsonOptions)}
            """;

        try
        {
            ChatResponse response = await ChatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, PlanFinalizerAgentPrompt.Text),
                    new ChatMessage(ChatRole.User, prompt),
                ],
                new ChatOptions
                {
                    Temperature = 0.2f,
                }).ConfigureAwait(false);

            string text = response.Text ?? "";
            string json = ExtractJsonObject(text);
            FinalPlanDraft? draft = JsonSerializer.Deserialize<FinalPlanDraft>(json, JsonOptions);
            if (draft is not null)
            {
                return draft;
            }
        }
        catch
        {
        }

        return HeuristicFinalize(userPrompt, legs, destinations);
    }

    private static FinalPlanDraft HeuristicFinalize(
        string userPrompt,
        IReadOnlyList<WorkflowLeg> legs,
        IReadOnlyList<WorkflowDestination> destinations)
    {
        int preferredStars = PreferStars(userPrompt);
        List<FlightTools.FlightRecord> flights = legs
            .Where(leg => leg.Candidates.Count > 0)
            .Select(leg => leg.Candidates[0])
            .ToList();
        List<HotelTools.FindHotel> hotels = destinations
            .Select(destination =>
                destination.Hotels.OrderBy(hotel => Math.Abs(hotel.Stars - preferredStars)).FirstOrDefault()
                ?? destination.Hotels.FirstOrDefault())
            .Where(hotel => hotel is not null)
            .Cast<HotelTools.FindHotel>()
            .ToList();

        string summary = string.IsNullOrWhiteSpace(userPrompt)
            ? "Package tour plan"
            : userPrompt.Length > 120 ? userPrompt[..120] : userPrompt;
        return new FinalPlanDraft(summary, flights, hotels);
    }

    private static int PreferStars(string userPrompt)
    {
        string lower = userPrompt.ToLowerInvariant();
        if (lower.Contains("premium") || lower.Contains("luxus") || lower.Contains("5 sterne") || lower.Contains("5-star"))
        {
            return 5;
        }

        if (lower.Contains("günstig") || lower.Contains("gunstig") || lower.Contains("cheap") || lower.Contains("budget"))
        {
            return 3;
        }

        return 4;
    }

    private static string ExtractJsonObject(string text)
    {
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return text[start..(end + 1)];
        }

        return text;
    }
}
