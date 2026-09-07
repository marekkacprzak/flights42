using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using AGUI.Abstractions;
using AGUI.Server;
using AiCsServer.Dashboard;
using AiCsServer.Data;
using AiCsServer.Infrastructure;
using AiCsServer.Prompts;
using AiCsServer.Tools;
using AiCsServer.Workflows;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;

namespace AiCsServer.Agents;

public static class AgentCatalog
{
    public const string OpenTelemetrySourceName = "AiCsServer";

    public static readonly string[] AgentIds =
    [
        "ticketingAgent",
        "planningAgent",
        "packageAgent",
        "hotelAgent",
        "travelPlannerAgent",
        "travelRefinementAgent",
        "reportingAgent",
        "checkinAgent",
        "dashboardAgent",
    ];

    private static readonly string[] PlanToolNames =
    [
        "getTravelPlan",
        "setTravelPlan",
        "addFlightToPlan",
        "removeFlightFromPlan",
        "replaceFlightInPlan",
        "addHotelToPlan",
        "removeHotelFromPlan",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static void RegisterAgents(WebApplicationBuilder builder, IChatClient chatClient)
    {
        Register(
            builder,
            chatClient,
            "hotelAgent",
            "Flight42 Hotel Agent",
            HotelAgentPrompt.Text,
            WithOptionalFindHotels([]));

        Register(
            builder,
            chatClient,
            "ticketingAgent",
            "Flight42 Ticketing Assistant",
            TicketingAgentPrompt.Text,
            CreateTicketingTools);

        Register(
            builder,
            chatClient,
            "planningAgent",
            "Flight42 Co-Planner",
            PlanningAgentPrompt.Text,
            [
                AIFunctionFactory.Create(FlightTools.FindBookedFlights, name: "findBookedFlights"),
            ]);

        Register(
            builder,
            chatClient,
            "packageAgent",
            "Flight42 Package Agent",
            PackageAgentPrompt.Text,
            [
                AIFunctionFactory.Create(PackageTourWorkflow.RunAsync, name: "packageTourWorkflow"),
            ]);

        Register(
            builder,
            chatClient,
            "travelPlannerAgent",
            "Flight42 Travel Planner",
            TravelPlannerAgentPrompt.Text,
            [
                AIFunctionFactory.Create(PackageTourWorkflow.RunAsync, name: "packageTourWorkflow"),
            ]);

        Register(
            builder,
            chatClient,
            "travelRefinementAgent",
            "Flight42 Travel Refinement",
            TravelRefinementAgentPrompt.Text,
            [
                AIFunctionFactory.Create(FlightTools.SearchFlights, name: "searchFlights"),
                AIFunctionFactory.Create(HotelTools.FindHotels, name: "findHotels"),
                AIFunctionFactory.Create(PlanTools.GetTravelPlan, name: "getTravelPlan"),
                AIFunctionFactory.Create(PlanTools.SetTravelPlan, name: "setTravelPlan"),
                AIFunctionFactory.Create(PlanTools.AddFlightToPlan, name: "addFlightToPlan"),
                AIFunctionFactory.Create(PlanTools.RemoveFlightFromPlan, name: "removeFlightFromPlan"),
                AIFunctionFactory.Create(PlanTools.ReplaceFlightInPlan, name: "replaceFlightInPlan"),
                AIFunctionFactory.Create(PlanTools.AddHotelToPlan, name: "addHotelToPlan"),
                AIFunctionFactory.Create(PlanTools.RemoveHotelFromPlan, name: "removeHotelFromPlan"),
            ]);

        Register(
            builder,
            chatClient,
            "reportingAgent",
            "Flight42 Reporting Assistant",
            ReportingAgentPrompt.Text,
            [
                AIFunctionFactory.Create(ExecuteJavaScriptTools.ExecuteJavaScript, name: "executeJavaScript"),
                AIFunctionFactory.Create(ChartTools.RenderChart, name: "renderChart"),
            ]);

        Register(
            builder,
            chatClient,
            "checkinAgent",
            "Flights42 Check-in Assistant",
            CheckinAgentPrompt.Text,
            []);

        Register(
            builder,
            chatClient,
            "dashboardAgent",
            "Flight42 Dashboard Composer",
            DashboardAgentPrompt.Text,
            [
                AIFunctionFactory.Create(DashboardTools.RenderDashboard, name: "renderDashboard"),
                AIFunctionFactory.Create(FlightTools.FindBookedFlights, name: "findBookedFlights"),
                AIFunctionFactory.Create(HotelTools.SearchHotels, name: "searchHotels"),
                AIFunctionFactory.Create(RentalCarTools.SearchRentalCars, name: "searchRentalCars"),
                AIFunctionFactory.Create(WeatherForecastTool.GetWeatherForecast, name: "weatherForecast"),
            ]);
    }

    public static void MapAgUiEndpoints(WebApplication app)
    {
        AGUIStreamOptions streamOptions = CreateSharedStreamOptions();

        foreach (string name in AgentIds)
        {
            app.MapAGUIServer(name, $"/ag-ui/{name}")
                .WithMetadata(streamOptions);
        }
    }

    private static AITool[] CreateTicketingTools(IServiceProvider services)
    {
        List<AITool> tools =
        [
            AIFunctionFactory.Create(FlightTools.FindBookedFlights, name: "findBookedFlights"),
            AIFunctionFactory.Create(FlightTools.BookFlight, name: "bookFlight"),
            AIFunctionFactory.Create(FlightTools.CancelFlight, name: "cancelFlight"),
            AIFunctionFactory.Create(RenderA2uiTool.RenderA2ui, name: RenderA2uiTool.ToolName),
            AIFunctionFactory.Create(ShowBoardingPassTool.ShowBoardingPassAsync, name: ShowBoardingPassTool.ToolName),
        ];

        if (!FeatureFlags.UseMcp)
        {
            AIAgent hotelAgent = services.GetRequiredKeyedService<AIAgent>("hotelAgent");
            tools.Add(hotelAgent.AsAIFunction());
        }

        return tools.ToArray();
    }

    private static AGUIStreamOptions CreateSharedStreamOptions()
    {
        AGUIStreamOptions options = new AGUIStreamOptions();

        foreach (string toolName in PlanToolNames)
        {
            options = options.MapResult(toolName, frc =>
            [
                new StateSnapshotEvent
                {
                    Snapshot = JsonSerializer.SerializeToElement(TravelPlanStore.Read(), JsonOptions),
                },
            ]);
        }

        options = options.MapResult(RenderA2uiTool.ToolName, MapA2uiSurfaceResult);
        options = options.MapResult(ShowBoardingPassTool.ToolName, MapA2uiSurfaceResult);

        options = options.MapCall(DashboardTools.RenderDashboardToolName, fcc =>
        {
            if (fcc.Arguments is not null)
            {
                PendingDashboardArgs[fcc.CallId] = JsonSerializer.SerializeToElement(fcc.Arguments, JsonOptions);
            }

            return Array.Empty<BaseEvent>();
        });

        options = options.MapResult(DashboardTools.RenderDashboardToolName, frc =>
        {
            JsonElement snapshot = NormalizeToJsonElement(frc.Result);
            PendingDashboardArgs.TryRemove(frc.CallId, out JsonElement callArgs);
            if (!HasNonEmptyArrayProperty(snapshot, "operations"))
            {
                JsonElement? recompiled = TryRecompileDashboard(snapshot, callArgs);
                if (recompiled is JsonElement compiled)
                {
                    snapshot = compiled;
                }
            }

            string surfaceId = TryGetStringProperty(snapshot, "surfaceId") ?? Guid.NewGuid().ToString("N");
            JsonElement operations = TryGetOperations(snapshot, "operations")
                ?? JsonSerializer.SerializeToElement(Array.Empty<object>(), JsonOptions);
            JsonElement content = JsonSerializer.SerializeToElement(new { operations }, JsonOptions);
            return new BaseEvent[]
            {
                new StateSnapshotEvent { Snapshot = snapshot },
                new ActivitySnapshotEvent
                {
                    MessageId = surfaceId,
                    ActivityType = "a2ui-surface",
                    Content = content,
                    Replace = true,
                },
            };
        });

        return options;
    }

    private static readonly ConcurrentDictionary<string, JsonElement> PendingDashboardArgs = new(StringComparer.Ordinal);


    private static BaseEvent[] MapA2uiSurfaceResult(FunctionResultContent frc)
    {
        if (!TryNormalizeToJsonElement(frc.Result, out JsonElement result))
        {
            return Array.Empty<BaseEvent>();
        }

        if (result.ValueKind == JsonValueKind.Object &&
            result.TryGetProperty("ok", out JsonElement ok) &&
            ok.ValueKind == JsonValueKind.False)
        {
            return Array.Empty<BaseEvent>();
        }

        JsonElement? operations = TryGetOperations(result, "messages")
            ?? TryGetOperations(result, "operations");
        if (operations is not JsonElement ops ||
            ops.ValueKind != JsonValueKind.Array ||
            ops.GetArrayLength() == 0)
        {
            return Array.Empty<BaseEvent>();
        }

        string surfaceId = TryGetStringProperty(result, "surfaceId") ?? Guid.NewGuid().ToString("N");
        JsonElement content = JsonSerializer.SerializeToElement(new { operations = ops }, JsonOptions);
        return
        [
            new ActivitySnapshotEvent
            {
                MessageId = surfaceId,
                ActivityType = "a2ui-surface",
                Content = content,
                Replace = true,
            },
        ];
    }

    private static bool TryNormalizeToJsonElement(object? result, out JsonElement element)
    {
        try
        {
            element = NormalizeToJsonElement(result);
            return element.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }
        catch (JsonException)
        {
            element = default;
            return false;
        }
    }

    private static JsonElement NormalizeToJsonElement(object? result)
    {
        switch (result)
        {
            case JsonElement je when je.ValueKind == JsonValueKind.String:
            {
                string? raw = je.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return JsonSerializer.SerializeToElement(new { }, JsonOptions);
                }

                if (raw[0] is not ('{' or '['))
                {
                    return JsonSerializer.SerializeToElement(new { ok = false, error = raw }, JsonOptions);
                }

                using JsonDocument doc = JsonDocument.Parse(raw);
                return doc.RootElement.Clone();
            }
            case JsonElement je when je.ValueKind == JsonValueKind.Object || je.ValueKind == JsonValueKind.Array:
                return je;
            case string text when !string.IsNullOrWhiteSpace(text):
            {
                if (text[0] is not ('{' or '['))
                {
                    return JsonSerializer.SerializeToElement(new { ok = false, error = text }, JsonOptions);
                }

                using JsonDocument doc = JsonDocument.Parse(text);
                return doc.RootElement.Clone();
            }
            case JsonNode node:
                return node.Deserialize<JsonElement>();
            case null:
                return JsonSerializer.SerializeToElement(new { }, JsonOptions);
            default:
                return JsonSerializer.SerializeToElement(result, JsonOptions);
        }
    }

    private static string? TryGetStringProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static JsonElement? TryGetOperations(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return value;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            string? raw = value.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return doc.RootElement.Clone();
            }
        }

        return null;
    }

    private static bool HasNonEmptyArrayProperty(JsonElement element, string name)
    {
        JsonElement? ops = TryGetOperations(element, name);
        return ops is JsonElement arr && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0;
    }

    private static JsonElement? TryRecompileDashboard(JsonElement snapshot, JsonElement callArgs)
    {
        JsonElement specElement = default;
        bool haveSpec = false;
        if (snapshot.ValueKind == JsonValueKind.Object && snapshot.TryGetProperty("tiles", out _))
        {
            specElement = snapshot;
            haveSpec = true;
        }
        else if (callArgs.ValueKind == JsonValueKind.Object)
        {
            specElement = callArgs;
            haveSpec = true;
        }

        if (!haveSpec)
        {
            return null;
        }

        try
        {
            DashboardSpec? spec = JsonSerializer.Deserialize<DashboardSpec>(specElement.GetRawText(), JsonOptions);
            if (spec is null || spec.Tiles.Count == 0)
            {
                return null;
            }

            CompiledDashboard compiled = CompileDashboard.CompileAsync(
                spec,
                catalogId: DashboardRequestAmbient.CurrentCatalogId).GetAwaiter().GetResult();
            return JsonSerializer.SerializeToElement(new
            {
                ok = true,
                surfaceId = compiled.SurfaceId,
                operations = compiled.Structural.Concat(compiled.DataModel).ToArray(),
                dataSteps = compiled.DataSteps,
            }, JsonOptions);
        }
        catch
        {
            return null;
        }
    }


    private static AITool[] WithOptionalFindHotels(AITool[] baseTools)
    {
        if (FeatureFlags.UseMcpApps)
        {
            return baseTools;
        }

        List<AITool> tools = [.. baseTools];
        tools.Add(AIFunctionFactory.Create(HotelTools.FindHotels, name: "findHotels"));
        return tools.ToArray();
    }

    private static void Register(
        WebApplicationBuilder builder,
        IChatClient chatClient,
        string name,
        string description,
        string instructions,
        AITool[] tools)
    {
        Register(builder, chatClient, name, description, instructions, _ => tools);
    }

    private static void Register(
        WebApplicationBuilder builder,
        IChatClient chatClient,
        string name,
        string description,
        string instructions,
        Func<IServiceProvider, AITool[]> toolsFactory)
    {
        builder
            .AddAIAgent(name, (services, agentName) => chatClient.AsAIAgent(
                    instructions: instructions,
                    name: agentName,
                    description: description,
                    tools: toolsFactory(services),
                    services: services)
                .AsBuilder()
                .UseOpenTelemetry(sourceName: OpenTelemetrySourceName)
                .Build())
            .WithInMemorySessionStore(withIsolation: false);
    }
}
