using AiCsServer.Data;

namespace AiCsServer.Routes;

public static class BookingsEndpoints
{
    public static void MapBookings(this WebApplication app)
    {
        app.MapGet("/bookings", async (CancellationToken cancellationToken) =>
        {
            IReadOnlyList<BookedFlight> flights = await BookedFlightsStore.GetBookedFlightsAsync(cancellationToken)
                .ConfigureAwait(false);
            return Results.Json(new { flights });
        });

        app.MapPost("/bookings/{flightId:int}", async (int flightId, CancellationToken cancellationToken) =>
        {
            if (BookedFlightsStore.IsBooked(flightId))
            {
                return Results.Json(
                    new
                    {
                        ok = false,
                        result = $"Flight {flightId} is already booked.",
                        code = "ALREADY_BOOKED",
                    },
                    statusCode: StatusCodes.Status409Conflict);
            }

            BookedFlight? flight = await BookedFlightsStore.FetchFlightAsync(flightId, cancellationToken)
                .ConfigureAwait(false);
            if (flight is null)
            {
                return Results.Json(
                    new
                    {
                        ok = false,
                        result = $"Flight {flightId} does not exist.",
                        code = "NOT_FOUND",
                    },
                    statusCode: StatusCodes.Status404NotFound);
            }

            BookedFlightsStore.AddBooking(flightId);
            return Results.Json(new
            {
                ok = true,
                result = $"Booked flight {flightId}.",
                flight,
            });
        });

        app.MapDelete("/bookings/{flightId:int}", async (int flightId, CancellationToken cancellationToken) =>
        {
            if (!BookedFlightsStore.IsBooked(flightId))
            {
                return Results.Json(
                    new
                    {
                        ok = false,
                        result = $"Flight {flightId} is not booked.",
                        code = "NOT_BOOKED",
                    },
                    statusCode: StatusCodes.Status404NotFound);
            }

            BookedFlight? flight = null;
            try
            {
                flight = await BookedFlightsStore.FetchFlightAsync(flightId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
            }

            BookedFlightsStore.RemoveBooking(flightId);

            if (flight is null)
            {
                return Results.Json(
                    new
                    {
                        ok = false,
                        result = $"Flight {flightId} could not be loaded after cancellation.",
                        code = "NOT_FOUND",
                    },
                    statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Json(new
            {
                ok = true,
                result = $"Cancelled flight {flightId}.",
                flight,
            });
        });
    }
}
