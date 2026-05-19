import { dashboardAgentPrompt } from './dashboard-agent.prompt.js';

// "Multi-step charts" comparison prompt.
//
// Identical to the fast `dashboardAgentPrompt` except that
// `renderFlightChartTool` (the composite "search + aggregate + render in
// one tool") is forbidden. Charts MUST be produced by the explicit
// chain `searchFlightsTool` → `aggregateDataTool` → `renderChartTool`.
//
// This prompt is derived from the fast one via targeted string
// replacements so the layout / data-binding rules stay in sync. Any new
// non-chart tile we add to the fast prompt automatically reaches the
// slow one too.

const PREFERRED_TOOL_BLOCK = `### Preferred: \`searchFlightsTool\` + \`renderFlightChartTool\` (two round-trips)

For the standard delay-related tiles (on-time vs. delayed share,
delays per day):

1. \`searchFlightsTool({ from, to })\` — fetch flights once per route.
   Reuse the returned \`flights\` array for the route's flight tables
   AND every chart on the same route. Never call it twice for the
   same \`{ from, to }\` in one turn.
2. \`renderFlightChartTool({ flights, type, chartType, date?, title? })\`
   — pass the array from step 1. The tool aggregates and renders the
   chart in one call, so you do not need \`aggregateDataTool\` /
   \`renderChartTool\` for these standard tiles.

Argument shape:

    { "flights": [...],   // from searchFlightsTool — reuse, don't refetch
      "type": "delayShare" | "delaysPerDay",
      "chartType": "bar" | "pie",
      "date": "2026-04-11",  // optional, ISO date prefix YYYY-MM-DD
      "title": "..." }

Returns \`{ url, stats }\`. Use \`url\` as-is in an \`updateDataModel\`,
and feel free to surface numbers from \`stats\` next to the chart
without an extra aggregation call.

### Fallback: \`aggregateDataTool\` + \`renderChartTool\` (custom aggregation)

For non-standard aggregations (custom groupings, ratios, percentages,
or charts that are not about delays):`;

const REQUIRED_CHAIN_BLOCK = `### Required: \`searchFlightsTool\` + \`aggregateDataTool\` + \`renderChartTool\`

In this run, every chart MUST be produced by chaining three tools (one
round-trip per tool). \`renderFlightChartTool\` is NOT available. For
each chart:

1. \`searchFlightsTool({ from, to })\` — fetches the raw flights.
   Reuse the result if you already searched the same route earlier in
   this turn.
2. \`aggregateDataTool({ data, expression })\` — pass the flight array
   as \`data\` and a JSONata expression that produces the chart
   aggregates. Examples for the standard delay charts:
   - delay share (pie):
       expression: \`{ "onTime": $count(data[delay = 0]),
                      "delayed": $count(data[delay > 0]) }\`
   - delays per day (bar, two series):
       expression: \`data{$substring(date, 0, 10): {
                      "onTime": $count($[delay = 0]),
                      "delayed": $count($[delay > 0]) }}\`
3. \`renderChartTool({ type, title, labels, datasets })\` — pass the
   aggregated values from step 2.

The same chain applies to non-standard aggregations:`;

const TILE_CHARTS_BLOCK = `### Charts (on-time vs. delayed, delays per day, …)

For the standard delay charts, call \`searchFlightsTool({ from, to })\`
ONCE per route (reuse the array for tables and every chart on the same
route) and then one \`renderFlightChartTool\` per chart:

- "On-time vs. delayed share":
  \`renderFlightChartTool({ flights, type: "delayShare", chartType: "pie" | "bar", date? })\`
- "Delays per day":
  \`renderFlightChartTool({ flights, type: "delaysPerDay", chartType: "bar" })\`

Bind the returned \`url\` into an \`Image\` via a data-model path like
\`/charts/<key>\`. For non-standard aggregations, use the fallback
documented above (\`aggregateDataTool\` + \`renderChartTool\`).`;

const TILE_CHARTS_BLOCK_SLOW = `### Charts (on-time vs. delayed, delays per day, …)

\`renderFlightChartTool\` is NOT available in this run. For every chart
(standard delay charts and non-standard aggregations alike) chain
\`searchFlightsTool\` → \`aggregateDataTool\` → \`renderChartTool\` as
described in "Charts" above.

- "On-time vs. delayed share":
  \`searchFlights({ from, to })\` → \`aggregateData\` with a JSONata
  expression like
  \`{ "onTime": $count(data[delay = 0]),
     "delayed": $count(data[delay > 0]) }\`
  → \`renderChart({ type: "pie", labels: ["On time", "Delayed"],
                  datasets: [{ label: "Flights", data: […] }] })\`.
- "Delays per day":
  \`searchFlights({ from, to })\` → \`aggregateData\` grouping by date
  prefix (e.g.
  \`data{$substring(date, 0, 10): {
     "onTime": $count($[delay = 0]),
     "delayed": $count($[delay > 0]) }}\`) → \`renderChart\` with
  \`type: "bar"\` and one dataset per "On time" / "Delayed" series.

Bind the returned \`url\` into an \`Image\` via a data-model path like
\`/charts/<key>\`.`;

const AVAILABLE_TOOLS_LINE = `Available data tools: \`searchFlightsTool\`, \`aggregateDataTool\`,
\`weatherForecastTool\`, \`findBookedFlightsTool\`, \`renderChartTool\`,
\`renderFlightChartTool\`, \`searchRentalCarsTool\`, \`searchHotelsTool\`.`;

const AVAILABLE_TOOLS_LINE_SLOW = `Available data tools: \`searchFlightsTool\`, \`aggregateDataTool\`,
\`weatherForecastTool\`, \`findBookedFlightsTool\`, \`renderChartTool\`,
\`searchRentalCarsTool\`, \`searchHotelsTool\`. (\`renderFlightChartTool\`
is intentionally NOT available — see "Charts".)`;

const BATCHING_FLIGHT_CHART_BULLET = `- Independent \`renderFlightChartTool\` calls (different directions or
  different aggregations) go in the same step.
`;

const URL_NOTE_BOTH_TOOLS = `2. Both \`renderFlightChartTool\` and \`renderChartTool\` return a
   SHORT URL like \`http://localhost:3001/charts/<id>.svg\`. Put it
   AS-IS as the value of an \`updateDataModel\` (e.g. path
   \`/charts/bar\`). Do not alter, shorten, or expand it. NEVER
   hand-build \`data:image/svg+xml;...\` URLs and NEVER use external
   chart services.`;

const URL_NOTE_RENDER_CHART_ONLY = `2. \`renderChartTool\` returns a SHORT URL like
   \`http://localhost:3001/charts/<id>.svg\`. Put it AS-IS as the value
   of an \`updateDataModel\` (e.g. path \`/charts/bar\`). Do not alter,
   shorten, or expand it. NEVER hand-build \`data:image/svg+xml;...\`
   URLs and NEVER use external chart services.`;

function applySlowOverrides(prompt: string): string {
  let next = prompt;

  if (!next.includes(PREFERRED_TOOL_BLOCK)) {
    throw new Error(
      'dashboard-slow-agent.prompt: "Preferred: renderFlightChartTool" block not found in fast prompt. Update the override.',
    );
  }
  next = next.replace(PREFERRED_TOOL_BLOCK, REQUIRED_CHAIN_BLOCK);

  if (!next.includes(TILE_CHARTS_BLOCK)) {
    throw new Error(
      'dashboard-slow-agent.prompt: tile-section "Charts" block not found in fast prompt. Update the override.',
    );
  }
  next = next.replace(TILE_CHARTS_BLOCK, TILE_CHARTS_BLOCK_SLOW);

  if (!next.includes(AVAILABLE_TOOLS_LINE)) {
    throw new Error(
      'dashboard-slow-agent.prompt: "Available data tools" line not found in fast prompt. Update the override.',
    );
  }
  next = next.replace(AVAILABLE_TOOLS_LINE, AVAILABLE_TOOLS_LINE_SLOW);

  if (!next.includes(BATCHING_FLIGHT_CHART_BULLET)) {
    throw new Error(
      'dashboard-slow-agent.prompt: batching bullet for renderFlightChartTool not found in fast prompt. Update the override.',
    );
  }
  next = next.replace(BATCHING_FLIGHT_CHART_BULLET, '');

  if (!next.includes(URL_NOTE_BOTH_TOOLS)) {
    throw new Error(
      'dashboard-slow-agent.prompt: "Both renderFlightChartTool and renderChartTool" note not found. Update the override.',
    );
  }
  next = next.replace(URL_NOTE_BOTH_TOOLS, URL_NOTE_RENDER_CHART_ONLY);

  return next;
}

export const dashboardSlowAgentPrompt =
  applySlowOverrides(dashboardAgentPrompt);
