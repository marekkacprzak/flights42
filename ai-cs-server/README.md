# ai-cs-server

ASP.NET Core port of Mastra `ai-server` + AG-UI hosting (Microsoft Agent Framework).

## Topology

```
Angular -> ai-cs-bff :3001 -> ai-cs-server :3011
             |-> mcp-server :3002 Streamable HTTP
             |-> QuickJS /internal/execute-javascript
```

- **Public entry:** `ai-cs-bff` on `http://localhost:3001` (what Angular/`ConfigService` calls).
- **Internal AG-UI host:** this .NET process on `http://127.0.0.1:3011`.
- Charts/images URLs in agent payloads use `AI_SERVER_PUBLIC_URL` (`http://localhost:3001`) so the browser hits the BFF.
- Hotels MCP Apps stay on Streamable HTTP (`mcp-server` or optional `ai-cs-mcp-server` at `:3002/mcp`).

## Run

Preferred AppHost: pnpm ai-cs-aspire

From the repo root (separate terminals):

```bash
pnpm mcp-server
pnpm ai-cs-server
pnpm ai-cs-bff
ng serve -o
```

Shortcut for .NET + BFF only:

```bash
pnpm ai-cs
```

Direct:

```bash
dotnet run --project ai-cs-server --urls http://127.0.0.1:3011
```

## LM Studio defaults

`appsettings.json` defaults to a local OpenAI-compatible LM Studio endpoint:

| Key                 | Default                        |
| ------------------- | ------------------------------ |
| `LLM_ENDPOINT`      | `http://192.168.8.199:1234/v1` |
| `OPENAI_API_KEY`    | `lm-studio`                    |
| `OPENAI_CHAT_MODEL` | `qwen/qwen3-vl-8b`             |

Override via config or environment for cloud providers. Reasoning tags from some LM Studio models are stripped by `StripLmStudioReasoningHandler`.

## Storage

Persistence uses **Microsoft.Data.Sqlite** only (no `Libsql.Client` dual-backend). Works on Apple Silicon / osx-arm64. Configure the DB file via `LibSql:Url` or `LIBSQL_URL` (SQLite `file:` URL or path); default `file:../flights42.db`.

`GET /health` reports `backend: "Microsoft.Data.Sqlite"` and `dbPath` (no `libsqlFailure`).

## Flags / config

| Name                                              | Default                                             | Purpose                                                                                                                                    |
| ------------------------------------------------- | --------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| `USE_MCP_APPS` / `UseMcpApps`                     | `true`                                              | Omit backend `findHotels` from ticketing/hotel agents so BFF-injected MCP UI tools stay **pending** and emit MCP Apps widgets              |
| `USE_MCP` / `UseMcp`                              | `false`                                             | Call MCP `findHotels` from .NET at `Mcp:HotelsUrl` (backend tool path; conflicts with Apps pending-tool strategy)                          |
| `USE_APPROVAL` / `UseApproval`                    | `true`                                              | Suspend bookFlight/cancelFlight with Mastra-shaped AG-UI tool_suspended interrupts until resume                                            |
| `OTEL_EXPORTER_OTLP_ENDPOINT`                     | `http://127.0.0.1:4317`                             | OTLP gRPC endpoint for traces, metrics, and logs                                                                                           |
| `Mcp:HotelsUrl`                                   | `http://127.0.0.1:3002/mcp`                         | Hotels MCP Streamable HTTP URL                                                                                                             |
| `Bff:ExecuteJavaScriptUrl` / `BFF_EXECUTE_JS_URL` | `http://127.0.0.1:3001/internal/execute-javascript` | QuickJS sandbox on the BFF                                                                                                                 |
| `LibSql:Url` / `LIBSQL_URL`                       | `file:../flights42.db`                              | SQLite file URL (Microsoft.Data.Sqlite; ARM/Apple Silicon OK). Key name kept for compatibility; seeds booked flights `1`, `2`, `50`, `516` |
| `AI_SERVER_PUBLIC_URL`                            | `http://localhost:3001`                             | Public base URL used in agent payloads (browser via BFF)                                                                                   |
| `Images:Root`                                     | `../ai-server/src/mastra/public/images`             | Static images root                                                                                                                         |

## Endpoints

AG-UI agents (via `MapAGUIServer`):

- `POST /ag-ui/ticketingAgent`
- `POST /ag-ui/planningAgent` (also remapped from plan mode by `AgentModeRemapMiddleware`)
- `POST /ag-ui/packageAgent`
- `POST /ag-ui/hotelAgent`
- `POST /ag-ui/travelPlannerAgent`
- `POST /ag-ui/travelRefinementAgent`
- `POST /ag-ui/reportingAgent`
- `POST /ag-ui/checkinAgent`
- `POST /ag-ui/dashboardAgent`

Other HTTP:

- `GET` / `POST` / `DELETE` `/bookings` …
- `GET /charts/{id}`
- `GET /images/{category}/{filename}`
- `POST /dashboard/compile`
- `GET /health`

## MCP Apps via BFF

MCP Apps run through **ai-cs-bff** (`MCPAppsMiddleware`), not in-process in this .NET host. The BFF injects MCP UI tools into `input.tools` and emits `ACTIVITY_SNAPSHOT` / `mcp-apps` for pending tool calls.

With `USE_MCP_APPS=true` (default), ticketing and hotel agents do not register backend `findHotels`, so the LLM only sees the BFF-injected MCP tool as a frontend tool.

## Shared AG-UI stream options

Every agent endpoint uses one shared `AGUIStreamOptions` builder (`CreateSharedStreamOptions`):

### Plan tools → `STATE_SNAPSHOT`

These travel-plan tools emit a `STATE_SNAPSHOT` with the **full current plan** from `TravelPlanStore.Read()` (`{ summary, flights, hotels }`), matching Mastra `emitStateSnapshot` after commit — not the tiny `{ added: id }` tool return:

- `getTravelPlan`
- `setTravelPlan`
- `addFlightToPlan`
- `removeFlightFromPlan`
- `replaceFlightInPlan`
- `addHotelToPlan`
- `removeHotelFromPlan`

Wired via `MapResult(toolName, …)` so plan-mode UI stays in sync (primarily `travelRefinementAgent`).

### `renderA2uiTool` → `ACTIVITY_SNAPSHOT`

- Registered on `ticketingAgent` as `renderA2uiTool` (`RenderA2uiTool.RenderA2ui`).
- Tool returns `JsonElement` `{ surfaceId, messages }`.
- `MapResult("renderA2uiTool")` emits `ACTIVITY_SNAPSHOT` with `activityType: "a2ui-surface"`, `messageId = surfaceId`, and `content: { operations: messages }` (same shape Angular / Mastra adapter use).

### Dashboard `renderDashboard`

- `renderDashboard` returns a `JsonElement` payload (`ok`, `surfaceId`, `operations`, `dataSteps`).
- `MapResult("renderDashboard")` emits both:
  - `STATE_SNAPSHOT` (full tool result), and
  - `ACTIVITY_SNAPSHOT` with `activityType: "a2ui-surface"` and `{ operations: [...] }`.
- No approval suspend is required for this mapping (do **not** also call `MapResultAsStateSnapshot` for the same tool — it would overwrite the mapper dictionary).
- Cache hits on `POST /ag-ui/dashboardAgent` short-circuit via **`DashboardCacheHitMiddleware`** (`UseDashboardCacheHit`) with SSE replay; cache misses fall through to `MapAGUIServer`.

## Human approval (`USE_APPROVAL`)

When `UseApproval=true` (default), `ApprovalInterruptChatClient` intercepts model `bookFlight` / `cancelFlight` calls after pre-checks pass and finishes the AG-UI run with reason `tool_suspended`. Metadata carries Mastra-compatible `suspendPayload` (`message` + `options` with payloads `{selection}` or `{approved}`), which Angular `chat-messages` renders as choice buttons.

Resume: client sends `RunAgentInput.resume` with the interrupt id and option payload. The server maps that to `InterruptResponseContent`, completes the booking/cancellation (`creditCard` | `miles`, or cancel/decline), injects `FunctionResultContent`, and continues the agent turn.

With `UseApproval=false`, book/cancel run immediately (book defaults to credit card).

`GET /health` exposes `useApproval`.

## hotelAgent as ticketing sub-agent

When `UseMcpApps=false`, ticketing registers `hotelAgent.AsAIFunction()` (same idea as Mastra `agents: { hotelAgent }`). The standalone `/ag-ui/hotelAgent` endpoint remains mapped. Local `findHotels` stays on the hotel agent only; ticketing does not also get a direct `findHotels` tool.

When `UseMcpApps=true`, neither local `findHotels` nor `hotelAgent` is added to ticketing (MCP Apps pending tools via BFF).

## Observability (OpenTelemetry / Aspire)

Traces, metrics, and logs export over OTLP. Chat client and agents use source name `AiCsServer`; providers also listen for `Experimental.Microsoft.Agents.AI`, `Experimental.Microsoft.Extensions.AI`, and `Experimental.AGUI.Server`.

Run the Aspire Dashboard locally (image `mcr.microsoft.com/dotnet/aspire-dashboard:latest`, publish 18888 and map host 4317 to container 18889), then open http://localhost:18888.

```bash
export OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317
pnpm ai-cs-server
```

## executeJavaScript via BFF

The `executeJavaScript` tool (`ExecuteJavaScriptTools`) POSTs `{ code, title }` to `Bff:ExecuteJavaScriptUrl` (default `http://127.0.0.1:3001/internal/execute-javascript`). The BFF runs QuickJS (`loadFlights` + `submitResult`) and returns chart-ready data.
