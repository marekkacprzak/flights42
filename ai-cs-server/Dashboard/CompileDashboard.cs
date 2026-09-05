using System.Text.Json;
using AiCsServer.Data;
using AiCsServer.Tools;

namespace AiCsServer.Dashboard;

public sealed record DataStep(string Name, object Args, object? Result = null);

public sealed record CompiledDashboard(
    string SurfaceId,
    IReadOnlyList<object> Structural,
    IReadOnlyList<object> DataModel,
    IReadOnlyList<DataStep> DataSteps);

public static class CompileDashboard
{
    private const int DefaultFlightTableMaxRows = 30;
    private const string FallbackCity = "Hamburg";

    public static async Task<CompiledDashboard> CompileAsync(DashboardSpec spec, string? surfaceId = null, string? catalogId = null)
    {
        List<DataStep> dataSteps = new();
        DashboardData data = await FetchAllAsync(spec, dataSteps).ConfigureAwait(false);

        string resolvedSurfaceId = string.IsNullOrWhiteSpace(surfaceId)
            ? $"dashboard-{Guid.NewGuid():N}"[..20]
            : surfaceId;
        string resolvedCatalogId = string.IsNullOrWhiteSpace(catalogId)
            ? "https://a2ui.org/specification/v0_9/catalogs/basic/catalog.json"
            : catalogId;
        List<object> components = new();
        List<string> rootChildren = new();
        List<object> dataOps = new();
        Dictionary<string, int> typeCounts = new(StringComparer.Ordinal);

        foreach (DashboardTile tile in spec.Tiles)
        {
            string baseId = NextTileId(tile.Type, typeCounts);
            TileBuildResult built = await BuildTileAsync(tile, baseId, data, dataSteps).ConfigureAwait(false);
            rootChildren.AddRange(built.RootChildren);
            components.AddRange(built.Components);
            dataOps.AddRange(built.DataOps);
        }

        List<object> structural =
        [
            new
            {
                version = "v0.9",
                createSurface = new
                {
                    surfaceId = resolvedSurfaceId,
                    catalogId = resolvedCatalogId,
                },
            },
            new
            {
                version = "v0.9",
                updateComponents = new
                {
                    surfaceId = resolvedSurfaceId,
                    components = new object[]
                    {
                        new
                        {
                            id = "root",
                            component = "Column",
                            children = rootChildren,
                        },
                    }.Concat(components).ToArray(),
                },
            },
        ];

        List<object> dataModel = dataOps
            .Select(op => PatchSurfaceId(op, resolvedSurfaceId))
            .ToList();

        return new CompiledDashboard(resolvedSurfaceId, structural, dataModel, dataSteps);
    }

    private static string NextTileId(string type, Dictionary<string, int> counts)
    {
        counts.TryGetValue(type, out int n);
        counts[type] = n + 1;
        return n == 0 ? type : $"{type}-{n + 1}";
    }

    private static async Task<DashboardData> FetchAllAsync(DashboardSpec spec, List<DataStep> dataSteps)
    {
        HashSet<string> routes = new(StringComparer.OrdinalIgnoreCase);
        bool needsBooked = false;
        foreach (DashboardTile tile in spec.Tiles)
        {
            switch (tile)
            {
                case FlightsTableTile t:
                    routes.Add(RouteKey(t.From, t.To));
                    break;
                case DelayedFlightsTableTile t:
                    routes.Add(RouteKey(t.From, t.To));
                    break;
                case DelayShareChartTile t:
                    routes.Add(RouteKey(t.From, t.To));
                    break;
                case DelaysPerDayChartTile t:
                    routes.Add(RouteKey(t.From, t.To));
                    break;
                case BoardingPassesTile:
                case BookedFlightsListTile:
                case WeatherListTile:
                    needsBooked = true;
                    break;
                case RentalCarsTile t when string.IsNullOrWhiteSpace(t.City):
                case HotelsTile t2 when string.IsNullOrWhiteSpace(t2.City):
                    needsBooked = true;
                    break;
            }
        }

        List<string> routeList = routes.ToList();
        List<int> flightStepIdx = new();
        foreach (string key in routeList)
        {
            string[] parts = key.Split('|');
            flightStepIdx.Add(Push(dataSteps, "searchFlights", new { from = parts[0], to = parts[1] }));
        }

        int? bookedIdx = needsBooked ? Push(dataSteps, "findBookedFlights", new { }) : null;

        Task<IReadOnlyList<BookedFlight>> bookedTask = needsBooked
            ? BookedFlightsStore.GetBookedFlightsAsync()
            : Task.FromResult<IReadOnlyList<BookedFlight>>(Array.Empty<BookedFlight>());
        Task<IReadOnlyList<FlightTools.FlightRecord>>[] flightTasks = routeList
            .Select(key =>
            {
                string[] parts = key.Split('|');
                return FlightTools.FetchFlightsAsync(parts[0], parts[1]);
            })
            .ToArray();

        IReadOnlyList<BookedFlight> booked = await bookedTask.ConfigureAwait(false);
        IReadOnlyList<FlightTools.FlightRecord>[] flights = await Task.WhenAll(flightTasks).ConfigureAwait(false);

        for (int i = 0; i < flightStepIdx.Count; i++)
        {
            dataSteps[flightStepIdx[i]] = dataSteps[flightStepIdx[i]] with
            {
                Result = new { count = flights[i].Count },
            };
        }

        if (bookedIdx is int bi)
        {
            dataSteps[bi] = dataSteps[bi] with { Result = new { count = booked.Count } };
        }

        Dictionary<string, IReadOnlyList<FlightTools.FlightRecord>> byRoute = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < routeList.Count; i++)
        {
            byRoute[routeList[i]] = flights[i];
        }

        return new DashboardData(booked, byRoute);
    }

    private static int Push(List<DataStep> steps, string name, object args)
    {
        steps.Add(new DataStep(name, args));
        return steps.Count - 1;
    }

    private static string RouteKey(string from, string to) => $"{from}|{to}";

    private static async Task<TileBuildResult> BuildTileAsync(
        DashboardTile tile,
        string baseId,
        DashboardData data,
        List<DataStep> dataSteps)
    {
        return tile switch
        {
            FlightsTableTile t => BuildFlightsTable(t, baseId, data, delayedOnly: false),
            DelayedFlightsTableTile t => BuildFlightsTable(
                new FlightsTableTile(t.From, t.To, t.MaxRows), baseId, data, delayedOnly: true),
            DelayShareChartTile t => BuildDelayShareChart(t, baseId, data),
            DelaysPerDayChartTile t => BuildDelaysPerDayChart(t, baseId, data),
            BoardingPassesTile t => BuildBoardingPasses(t, baseId, data),
            BookedFlightsListTile t => await BuildBookedFlightsListAsync(t, baseId, data, dataSteps).ConfigureAwait(false),
            FlightSearchTile t => BuildFlightSearch(t, baseId),
            RentalCarsTile t => BuildRentalCars(t, baseId, data, dataSteps),
            HotelsTile t => BuildHotels(t, baseId, data, dataSteps),
            WeatherListTile t => await BuildWeatherListAsync(t, baseId, data, dataSteps).ConfigureAwait(false),
            _ => new TileBuildResult([], [], []),
        };
    }

    private static TileBuildResult BuildFlightsTable(
        FlightsTableTile tile,
        string baseId,
        DashboardData data,
        bool delayedOnly)
    {
        IReadOnlyList<FlightTools.FlightRecord> all = data.FlightsByRoute.GetValueOrDefault(
            RouteKey(tile.From, tile.To),
            Array.Empty<FlightTools.FlightRecord>());
        IReadOnlyList<FlightTools.FlightRecord> filtered = delayedOnly
            ? all.Where(f => f.Delay > 0).ToArray()
            : all;
        int max = tile.MaxRows ?? DefaultFlightTableMaxRows;
        FlightTools.FlightRecord[] flights = filtered.Take(max).ToArray();

        string cardId = NodeId(baseId, "card");
        string bodyId = NodeId(baseId, "body");
        string titleId = NodeId(baseId, "title");
        string title = TitleFor(tile.From, tile.To, delayedOnly);

        if (flights.Length == 0)
        {
            string emptyId = NodeId(baseId, "empty");
            return new TileBuildResult(
                [cardId],
                [
                    new { id = cardId, component = "Card", child = bodyId },
                    new { id = bodyId, component = "Column", children = new[] { titleId, emptyId } },
                    new { id = titleId, component = "Text", text = title, variant = "h2" },
                    new
                    {
                        id = emptyId,
                        component = "Text",
                        text = delayedOnly
                            ? "No delayed flights for this route."
                            : "No flights found for this route.",
                        variant = "body",
                    },
                ],
                []);
        }

        string headerId = NodeId(baseId, "header");
        string lastColumnHeader = delayedOnly ? "Delay (min)" : "Status";
        string[] columnNames = ["flight", "date", "time", delayedOnly ? "delay" : "status"];
        string[] headerCellIds = columnNames.Select(name => $"{headerId}-{name}").ToArray();

        List<object> components = new();
        List<string> rowIds = new();
        List<object> flightRows = new();

        for (int j = 0; j < flights.Length; j++)
        {
            FlightTools.FlightRecord f = flights[j];
            string rowId = $"{NodeId(baseId, "row")}-{j + 1}";
            rowIds.Add(rowId);
            string[] cellIds = columnNames.Select(name => $"{rowId}-{name}").ToArray();
            string datePart = f.Date.Length >= 10 ? f.Date[..10] : f.Date;
            string timePart = f.Date.Length >= 16 ? f.Date.Substring(11, 5) : string.Empty;
            string status = delayedOnly
                ? f.Delay.ToString()
                : f.Delay > 0
                    ? $"Delayed by {f.Delay} min"
                    : "On time";

            components.Add(new { id = rowId, component = "Row", align = "stretch", children = cellIds });
            components.Add(CellText(cellIds[0], PathFor(baseId, $"flights/{j}/number")));
            components.Add(CellText(cellIds[1], PathFor(baseId, $"flights/{j}/date")));
            components.Add(CellText(cellIds[2], PathFor(baseId, $"flights/{j}/time")));
            components.Add(CellText(cellIds[3], PathFor(baseId, $"flights/{j}/status")));

            flightRows.Add(new
            {
                number = f.Id.ToString(),
                date = datePart,
                time = timePart,
                status,
            });
        }

        List<object> header = new()
        {
            new { id = cardId, component = "Card", child = bodyId },
            new
            {
                id = bodyId,
                component = "Column",
                children = new[] { titleId, headerId }.Concat(rowIds).ToArray(),
            },
            new { id = titleId, component = "Text", text = title, variant = "h2" },
            new { id = headerId, component = "Row", align = "stretch", children = headerCellIds },
            HeaderText(headerCellIds[0], "Flight"),
            HeaderText(headerCellIds[1], "Date"),
            HeaderText(headerCellIds[2], "Time"),
            HeaderText(headerCellIds[3], lastColumnHeader),
        };
        header.AddRange(components);

        return new TileBuildResult(
            [cardId],
            header,
            [DataOp(TilePath(baseId), new { flights = flightRows })]);
    }

    private static TileBuildResult BuildDelayShareChart(DelayShareChartTile tile, string baseId, DashboardData data)
    {
        IReadOnlyList<FlightTools.FlightRecord> flights = data.FlightsByRoute.GetValueOrDefault(
            RouteKey(tile.From, tile.To),
            Array.Empty<FlightTools.FlightRecord>());
        int delayed = flights.Count(f => f.Delay > 0);
        int onTime = Math.Max(0, flights.Count - delayed);
        string chartType = string.IsNullOrWhiteSpace(tile.ChartType) ? "pie" : tile.ChartType!;
        string url = ChartTools.BuildAndCacheChartUrl(
            chartType,
            ["On time", "Delayed"],
            [new ChartTools.ChartDataset("Flights", [onTime, delayed])],
            $"On-time vs. delayed ({tile.From} → {tile.To})");

        return BuildChartTile(
            baseId,
            $"Delay share {tile.From} → {tile.To}",
            url);
    }

    private static TileBuildResult BuildDelaysPerDayChart(DelaysPerDayChartTile tile, string baseId, DashboardData data)
    {
        IReadOnlyList<FlightTools.FlightRecord> flights = data.FlightsByRoute.GetValueOrDefault(
            RouteKey(tile.From, tile.To),
            Array.Empty<FlightTools.FlightRecord>());
        Dictionary<string, (int OnTime, int Delayed)> buckets = new(StringComparer.Ordinal);
        foreach (FlightTools.FlightRecord f in flights)
        {
            string day = f.Date.Length >= 10 ? f.Date[..10] : FormatDate.ToDateOnly(f.Date);
            buckets.TryGetValue(day, out (int OnTime, int Delayed) bucket);
            if (f.Delay > 0)
            {
                bucket.Delayed += 1;
            }
            else
            {
                bucket.OnTime += 1;
            }

            buckets[day] = bucket;
        }

        KeyValuePair<string, (int OnTime, int Delayed)>[] sorted = buckets
            .OrderBy(kv => kv.Key)
            .Take(14)
            .ToArray();
        string[] labels = sorted.Length == 0 ? ["none"] : sorted.Select(kv => kv.Key).ToArray();
        double[] onTimeValues = sorted.Length == 0 ? [0] : sorted.Select(kv => (double)kv.Value.OnTime).ToArray();
        double[] delayedValues = sorted.Length == 0 ? [0] : sorted.Select(kv => (double)kv.Value.Delayed).ToArray();
        string url = ChartTools.BuildAndCacheChartUrl(
            "bar",
            labels,
            [
                new ChartTools.ChartDataset("On time", onTimeValues),
                new ChartTools.ChartDataset("Delayed", delayedValues),
            ],
            $"Delays per day ({tile.From} → {tile.To})");

        return BuildChartTile(
            baseId,
            $"Delays per day {tile.From} → {tile.To}",
            url);
    }

    private static TileBuildResult BuildChartTile(string baseId, string title, string chartUrl)
    {
        string cardId = NodeId(baseId, "card");
        string bodyId = NodeId(baseId, "body");
        string titleId = NodeId(baseId, "title");
        string chartId = NodeId(baseId, "chart");
        string chartPath = PathFor(baseId, "chart");
        return new TileBuildResult(
            [cardId],
            [
                new { id = cardId, component = "Card", child = bodyId },
                new { id = bodyId, component = "Column", children = new[] { titleId, chartId } },
                new { id = titleId, component = "Text", text = title, variant = "h2" },
                new { id = chartId, component = "Image", url = new { path = chartPath } },
            ],
            [DataOp(TilePath(baseId), new { chart = chartUrl })]);
    }

    private static string NodeId(string baseId, string suffix) => $"{baseId}-{suffix}";

    private static string PathFor(string baseId, string suffix) => $"/{baseId}/{suffix}";

    private static string TilePath(string baseId) => $"/{baseId}";

    private static string TitleFor(string from, string to, bool onlyDelayed) =>
        onlyDelayed ? $"Delayed flights {from} → {to}" : $"Flights {from} → {to}";

    private static object CellText(string id, string path) =>
        new { id, component = "Text", text = new { path }, weight = 1 };

    private static object HeaderText(string id, string label) =>
        new { id, component = "Text", text = label, variant = "h3", weight = 1 };

    private static object DataOp(string path, object value) =>
        new
        {
            version = "v0.9",
            updateDataModel = new
            {
                surfaceId = "pending",
                path,
                value,
            },
        };

    private static TileBuildResult BuildBoardingPasses(BoardingPassesTile tile, string baseId, DashboardData data)
    {
        int requested = tile.Count ?? 2;
        BookedFlight[] flights = SortBookedFlightsAscending(data.BookedFlights).Take(requested).ToArray();
        if (flights.Length == 0)
        {
            return new TileBuildResult([], [], []);
        }

        string stackId = NodeId(baseId, "stack");
        string[] ticketIds = flights.Select((_, j) => $"{NodeId(baseId, "ticket")}-{j + 1}").ToArray();
        List<object> components =
        [
            new { id = stackId, component = "Column", children = ticketIds },
        ];
        for (int j = 0; j < flights.Length; j++)
        {
            components.Add(new
            {
                id = ticketIds[j],
                component = "TicketWidget",
                ticketId = new { path = PathFor(baseId, $"tickets/{j}/ticketId") },
                from = new { path = PathFor(baseId, $"tickets/{j}/from") },
                to = new { path = PathFor(baseId, $"tickets/{j}/to") },
                date = new { path = PathFor(baseId, $"tickets/{j}/date") },
                delay = new { path = PathFor(baseId, $"tickets/{j}/delay") },
            });
        }

        object[] tickets = flights.Select(flight => new
        {
            ticketId = flight.Id,
            from = flight.From,
            to = flight.To,
            date = flight.Date.Length >= 10 ? flight.Date[..10] : flight.Date,
            delay = flight.Delay,
        }).ToArray<object>();

        return new TileBuildResult(
            [stackId],
            components,
            [DataOp(TilePath(baseId), new { tickets })]);
    }

    private static async Task<TileBuildResult> BuildBookedFlightsListAsync(
        BookedFlightsListTile tile,
        string baseId,
        DashboardData data,
        List<DataStep> dataSteps)
    {
        BookedFlight[] allBooked = SortBookedFlightsAscending(data.BookedFlights);
        BookedFlight[] flights = tile.MaxRows is int max ? allBooked.Take(max).ToArray() : allBooked;
        bool showCheckIn = tile.ShowCheckInButton ?? true;
        bool showWeather = tile.ShowWeather ?? true;

        string cardId = NodeId(baseId, "card");
        string bodyId = NodeId(baseId, "body");
        string titleId = NodeId(baseId, "title");

        if (flights.Length == 0)
        {
            string emptyId = NodeId(baseId, "empty");
            return new TileBuildResult(
                [cardId],
                [
                    new { id = cardId, component = "Card", child = bodyId },
                    new { id = bodyId, component = "Column", children = new[] { titleId, emptyId } },
                    new { id = titleId, component = "Text", text = "My booked flights", variant = "h2" },
                    new { id = emptyId, component = "Text", text = "You have no booked flights.", variant = "body" },
                ],
                []);
        }

        List<object> components = new();
        List<string> rowIds = new();
        List<object> flightRows = new();

        for (int j = 0; j < flights.Length; j++)
        {
            BookedFlight flight = flights[j];
            string rowId = $"{NodeId(baseId, "flight")}-{j + 1}";
            string colId = $"{rowId}-content";
            string titleNodeId = $"{rowId}-route";
            string metaId = $"{rowId}-details";
            string btnId = $"{rowId}-check-in";
            string btnLabelId = $"{btnId}-label";
            rowIds.Add(rowId);

            string[] colChildren = showCheckIn
                ? [titleNodeId, metaId, btnId]
                : [titleNodeId, metaId];

            components.Add(new { id = rowId, component = "Row", align = "start", children = new[] { colId } });
            components.Add(new { id = colId, component = "Column", weight = 3, children = colChildren });
            components.Add(new
            {
                id = titleNodeId,
                component = "Text",
                text = new { path = PathFor(baseId, $"flights/{j}/route") },
                variant = "h3",
            });
            components.Add(new
            {
                id = metaId,
                component = "Text",
                text = new { path = PathFor(baseId, $"flights/{j}/meta") },
                variant = "body",
            });

            if (showCheckIn)
            {
                components.Add(new
                {
                    id = btnId,
                    component = "Button",
                    child = btnLabelId,
                    action = new
                    {
                        @event = new
                        {
                            name = "checkIn",
                            context = new { flightId = new { path = PathFor(baseId, $"flights/{j}/id") } },
                        },
                    },
                });
                components.Add(new { id = btnLabelId, component = "Text", text = "Check in" });
            }

            string statusText = flight.Delay > 0 ? $"Delayed by {flight.Delay} min" : "On time";
            string day = flight.Date.Length >= 10 ? flight.Date[..10] : flight.Date;
            string meta;
            if (showWeather)
            {
                WeatherForecastTool.WeatherForecast w = WeatherForecastTool.GetWeatherForecast(flight.To, flight.Date);
                int step = Push(dataSteps, "weatherForecast", new { city = flight.To, date = day });
                dataSteps[step] = dataSteps[step] with
                {
                    Result = new { condition = w.Condition, temperatureC = w.TemperatureC },
                };
                meta = $"{day} · {WeatherIconFor(w.Condition)} {w.Condition} — {w.TemperatureC} °C · {statusText}";
            }
            else
            {
                meta = $"{day} · {statusText}";
            }

            flightRows.Add(new
            {
                id = flight.Id,
                route = $"{flight.From} → {flight.To}",
                meta,
            });
        }

        await Task.CompletedTask.ConfigureAwait(false);

        List<object> header =
        [
            new { id = cardId, component = "Card", child = bodyId },
            new { id = bodyId, component = "Column", children = new[] { titleId }.Concat(rowIds).ToArray() },
            new { id = titleId, component = "Text", text = "My booked flights", variant = "h2" },
        ];
        header.AddRange(components);

        return new TileBuildResult(
            [cardId],
            header,
            [DataOp(TilePath(baseId), new { flights = flightRows })]);
    }

    private static TileBuildResult BuildFlightSearch(FlightSearchTile tile, string baseId)
    {
        string cardId = NodeId(baseId, "card");
        string bodyId = NodeId(baseId, "body");
        string titleId = NodeId(baseId, "title");
        string fromId = NodeId(baseId, "from-field");
        string toId = NodeId(baseId, "to-field");
        string btnId = NodeId(baseId, "submit");
        string btnLabelId = $"{btnId}-label";
        string fromPath = PathFor(baseId, "search/from");
        string toPath = PathFor(baseId, "search/to");

        return new TileBuildResult(
            [cardId],
            [
                new { id = cardId, component = "Card", child = bodyId },
                new { id = bodyId, component = "Column", children = new[] { titleId, fromId, toId, btnId } },
                new { id = titleId, component = "Text", text = "Find a flight", variant = "h2" },
                new { id = fromId, component = "TextField", label = "From", value = new { path = fromPath } },
                new { id = toId, component = "TextField", label = "To", value = new { path = toPath } },
                new
                {
                    id = btnId,
                    component = "Button",
                    child = btnLabelId,
                    action = new
                    {
                        @event = new
                        {
                            name = "dashboardFlightSearch",
                            context = new
                            {
                                from = new { path = fromPath },
                                to = new { path = toPath },
                            },
                        },
                    },
                },
                new { id = btnLabelId, component = "Text", text = "Search" },
            ],
            [
                DataOp(
                    TilePath(baseId),
                    new
                    {
                        search = new
                        {
                            from = tile.DefaultFrom ?? "Graz",
                            to = tile.DefaultTo ?? "Hamburg",
                        },
                    }),
            ]);
    }

    private static TileBuildResult BuildRentalCars(
        RentalCarsTile tile,
        string baseId,
        DashboardData data,
        List<DataStep> dataSteps)
    {
        string city = ResolveCity(tile.City, data);
        int step = Push(dataSteps, "searchRentalCars", new { city });
        RentalCarTools.RentalCarSearchResult result = RentalCarTools.SearchRentalCars(city);
        RentalCarTools.RentalCar[] cars = result.Cars
            .Take(tile.MaxItems ?? result.Cars.Count)
            .ToArray();
        dataSteps[step] = dataSteps[step] with { Result = new { count = cars.Length } };

        return BuildImageRowList(
            baseId,
            $"Rent a car in {result.City}",
            cars.Select(car => new ImageRowItem(
                car.ImageUrl,
                $"{car.Category} — {car.Model}",
                $"From {car.PricePerDay} {car.Currency} / day")).ToArray());
    }

    private static TileBuildResult BuildHotels(
        HotelsTile tile,
        string baseId,
        DashboardData data,
        List<DataStep> dataSteps)
    {
        string city = ResolveCity(tile.City, data);
        int step = Push(dataSteps, "searchHotels", new { city });
        HotelTools.SearchHotelsResult result = HotelTools.SearchHotelsCore(city);
        HotelTools.SearchHotel[] hotels = result.Hotels
            .Take(tile.MaxItems ?? result.Hotels.Count)
            .ToArray();
        dataSteps[step] = dataSteps[step] with { Result = new { count = hotels.Length } };

        return BuildImageRowList(
            baseId,
            $"Hotels in {result.City}",
            hotels.Select(hotel => new ImageRowItem(
                hotel.ImageUrl,
                hotel.Name,
                $"{hotel.Stars}★ — from {hotel.PricePerNight} {hotel.Currency} / night")).ToArray());
    }

    private static async Task<TileBuildResult> BuildWeatherListAsync(
        WeatherListTile tile,
        string baseId,
        DashboardData data,
        List<DataStep> dataSteps)
    {
        BookedFlight[] allBooked = data.BookedFlights.ToArray();
        BookedFlight[] flights = tile.MaxRows is int max ? allBooked.Take(max).ToArray() : allBooked;

        string cardId = NodeId(baseId, "card");
        string bodyId = NodeId(baseId, "body");
        string titleId = NodeId(baseId, "title");

        if (flights.Length == 0)
        {
            string emptyId = NodeId(baseId, "empty");
            return new TileBuildResult(
                [cardId],
                [
                    new { id = cardId, component = "Card", child = bodyId },
                    new { id = bodyId, component = "Column", children = new[] { titleId, emptyId } },
                    new { id = titleId, component = "Text", text = "Weather at your destinations", variant = "h2" },
                    new { id = emptyId, component = "Text", text = "No upcoming destinations.", variant = "body" },
                ],
                []);
        }

        List<object> components = new();
        List<string> rowIds = new();
        List<object> itemRows = new();

        for (int j = 0; j < flights.Length; j++)
        {
            BookedFlight flight = flights[j];
            string lineId = $"{NodeId(baseId, "entry")}-{j + 1}";
            rowIds.Add(lineId);
            components.Add(new
            {
                id = lineId,
                component = "Text",
                text = new { path = PathFor(baseId, $"items/{j}/text") },
                variant = "body",
            });

            string day = flight.Date.Length >= 10 ? flight.Date[..10] : flight.Date;
            WeatherForecastTool.WeatherForecast w = WeatherForecastTool.GetWeatherForecast(flight.To, flight.Date);
            int step = Push(dataSteps, "weatherForecast", new { city = flight.To, date = day });
            dataSteps[step] = dataSteps[step] with
            {
                Result = new { condition = w.Condition, temperatureC = w.TemperatureC },
            };
            itemRows.Add(new
            {
                text = $"{flight.To} · {day} · {WeatherIconFor(w.Condition)} {w.Condition} — {w.TemperatureC} °C",
            });
        }

        await Task.CompletedTask.ConfigureAwait(false);

        List<object> header =
        [
            new { id = cardId, component = "Card", child = bodyId },
            new { id = bodyId, component = "Column", children = new[] { titleId }.Concat(rowIds).ToArray() },
            new { id = titleId, component = "Text", text = "Weather at your destinations", variant = "h2" },
        ];
        header.AddRange(components);

        return new TileBuildResult(
            [cardId],
            header,
            [DataOp(TilePath(baseId), new { items = itemRows })]);
    }

    private static TileBuildResult BuildImageRowList(string baseId, string title, IReadOnlyList<ImageRowItem> items)
    {
        string cardId = NodeId(baseId, "card");
        string bodyId = NodeId(baseId, "body");
        string titleId = NodeId(baseId, "title");

        List<object> components = new();
        List<string> rowIds = new();
        List<object> itemRows = new();

        for (int j = 0; j < items.Count; j++)
        {
            ImageRowItem item = items[j];
            string rowId = $"{NodeId(baseId, "item")}-{j + 1}";
            string imgId = $"{rowId}-image";
            string colId = $"{rowId}-content";
            string titleNodeId = $"{rowId}-title";
            string subId = $"{rowId}-subtitle";
            rowIds.Add(rowId);

            components.Add(new { id = rowId, component = "Row", align = "start", children = new[] { imgId, colId } });
            components.Add(new { id = imgId, component = "Image", url = new { path = PathFor(baseId, $"items/{j}/image") }, weight = 1 });
            components.Add(new { id = colId, component = "Column", weight = 3, children = new[] { titleNodeId, subId } });
            components.Add(new
            {
                id = titleNodeId,
                component = "Text",
                text = new { path = PathFor(baseId, $"items/{j}/title") },
                variant = "h3",
            });
            components.Add(new
            {
                id = subId,
                component = "Text",
                text = new { path = PathFor(baseId, $"items/{j}/subtitle") },
                variant = "body",
            });

            itemRows.Add(new { image = item.ImageUrl, title = item.Title, subtitle = item.Subtitle });
        }

        List<object> header =
        [
            new { id = cardId, component = "Card", child = bodyId },
            new { id = bodyId, component = "Column", children = new[] { titleId }.Concat(rowIds).ToArray() },
            new { id = titleId, component = "Text", text = title, variant = "h2" },
        ];
        header.AddRange(components);

        return new TileBuildResult(
            [cardId],
            header,
            [DataOp(TilePath(baseId), new { items = itemRows })]);
    }

    private static BookedFlight[] SortBookedFlightsAscending(IEnumerable<BookedFlight> flights) =>
        flights.OrderBy(f => f.Date, StringComparer.Ordinal).ToArray();

    private static string WeatherIconFor(string condition) => condition switch
    {
        "Sunny" => "☀️",
        "Partly cloudy" => "⛅",
        "Cloudy" => "☁️",
        "Rain" => "🌧️",
        "Thunder" => "⛈️",
        _ => "🌤️",
    };

    private static string ResolveCity(string? city, DashboardData data)
    {
        if (!string.IsNullOrWhiteSpace(city))
        {
            return city.Trim();
        }

        BookedFlight? next = data.BookedFlights.FirstOrDefault();
        return next?.To ?? FallbackCity;
    }


    private static object PatchSurfaceId(object op, string surfaceId)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(op);
        using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("updateDataModel", out System.Text.Json.JsonElement model))
        {
            return op;
        }

        Dictionary<string, object?> modelObj = new()
        {
            ["surfaceId"] = surfaceId,
            ["path"] = model.TryGetProperty("path", out System.Text.Json.JsonElement path) ? path.GetString() : "/",
            ["value"] = model.TryGetProperty("value", out System.Text.Json.JsonElement value)
                ? System.Text.Json.JsonSerializer.Deserialize<object>(value.GetRawText())
                : null,
        };
        return new { version = "v0.9", updateDataModel = modelObj };
    }

    private sealed record DashboardData(
        IReadOnlyList<BookedFlight> BookedFlights,
        IReadOnlyDictionary<string, IReadOnlyList<FlightTools.FlightRecord>> FlightsByRoute);

    private sealed record ImageRowItem(string ImageUrl, string Title, string Subtitle);

    private sealed record TileBuildResult(
        IReadOnlyList<string> RootChildren,
        IReadOnlyList<object> Components,
        IReadOnlyList<object> DataOps);
}
