using System.ComponentModel;
using System.Text.Json;
using AiCsServer.Dashboard;
using AiCsServer.Data;

namespace AiCsServer.Tools;

public static class ShowBoardingPassTool
{
    public const string ToolName = "showBoardingPass";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Description("""
        Show a boarding-pass TicketWidget (barcode/QR) for one flight.
        Use this when the user asks for a ticket / boarding pass / TicketWidget.
        Pass the flight id from findBookedFlights (or the id the user named).
        Do NOT use flightWidget or renderA2uiTool for boarding-pass intents —
        this tool builds the A2UI surface server-side.
        """)]
    public static async Task<JsonElement> ShowBoardingPassAsync(
        [Description("Flight id to render as a boarding pass.")] int flightId,
        CancellationToken cancellationToken = default)
    {
        BookedFlight? flight = await BookedFlightsStore.FetchFlightAsync(flightId, cancellationToken)
            .ConfigureAwait(false);
        if (flight is null)
        {
            return JsonSerializer.SerializeToElement(new
            {
                ok = false,
                error = $"Flight {flightId} was not found.",
            }, JsonOptions);
        }

        string? ambientCatalog = DashboardRequestAmbient.CurrentCatalogId;
        string catalogId = string.IsNullOrWhiteSpace(ambientCatalog)
            ? "https://example.com/catalogs/flights42-a2ui-demo"
            : ambientCatalog;

        string surfaceId = $"boarding-{flight.Id}-{Guid.NewGuid():N}";

        string date = flight.Date;
        if (date.Length >= 10)
        {
            date = date[..10];
        }

        object[] messages =
        [
            new
            {
                version = "v0.9",
                createSurface = new { surfaceId, catalogId },
            },
            new
            {
                version = "v0.9",
                updateComponents = new
                {
                    surfaceId,
                    components = new object[]
                    {
                        new
                        {
                            id = "root",
                            component = "Column",
                            children = new[] { "heading", "ticket" },
                        },
                        new
                        {
                            id = "heading",
                            component = "Text",
                            text = $"Boarding pass — {flight.From} → {flight.To}",
                        },
                        new
                        {
                            id = "ticket",
                            component = "TicketWidget",
                            ticketId = new { path = "/ticket/ticketId" },
                            from = new { path = "/ticket/from" },
                            to = new { path = "/ticket/to" },
                            date = new { path = "/ticket/date" },
                            delay = new { path = "/ticket/delay" },
                        },
                    },
                },
            },
            new
            {
                version = "v0.9",
                updateDataModel = new
                {
                    surfaceId,
                    path = "/ticket",
                    value = new
                    {
                        ticketId = flight.Id,
                        from = flight.From,
                        to = flight.To,
                        date,
                        delay = flight.Delay,
                    },
                },
            },
        ];

        return JsonSerializer.SerializeToElement(new
        {
            ok = true,
            surfaceId,
            messages,
            flight,
        }, JsonOptions);
    }
}
