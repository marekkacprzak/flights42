# ai-cs-bff

Node/TypeScript Backend-For-Frontend for flights42 MCP Apps and QuickJS sandbox.

Microsoft Agent Framework (MAF) does not provide an MCP Apps runtime.
`MCPAppsMiddleware` lives outside `MapAGUIServer`, so this BFF sits between
Angular and the .NET AG-UI host.

## Topology

```
[ Angular ] --POST :3001/ag-ui/{agentId}--> [ ai-cs-bff Node ] --raw SSE http.request--> [ ai-cs-server .NET :3011 ]
                                              | cached MCP UI tool injection (+ MCPAppsMiddleware for __proxiedMCPRequest)
                                              └──> [ mcp-server :3002/mcp Streamable HTTP ]
```

- **Public entry:** this BFF on `:3001` — Angular/`ConfigService` talks here.
- **Internal host:** .NET AG-UI + bookings/charts/images/dashboard on `:3011`.
- Hotels MCP Apps stay on the existing TypeScript `mcp-server` (`:3002`).
  Optional alternate: `ai-cs-mcp-server` (same Streamable HTTP URL for `MCP_URL`).
  Use Streamable HTTP for Apps (not a .NET WebSocket MCP server).

### Transport note

`MCPAppsMiddleware` is configured with `{ type: "http", url }`. That uses the
MCP Streamable HTTP client — not WebSocket.

## Responsibilities

1. `POST /ag-ui/:agentId` — validate `RunAgentInput`, inject cached MCP UI
   tools into `input.tools`, then **raw-proxy** SSE from
   `DOTNET_AGUI_URL/ag-ui/{agentId}` with Node `http.request` (byte-stream
   passthrough so `RUN_STARTED` / tool events are not buffered).
2. Proxy `/bookings`, `/charts`, `/images`, `/dashboard` (including
   `POST /dashboard/compile`), and `/health` to .NET so Angular only needs `:3001`.
3. Handle `__proxiedMCPRequest` via `MCPAppsMiddleware` + `HttpAgent` (short
   request/response for iframe resource reads / tool calls).
4. `POST /internal/execute-javascript` — QuickJS sandbox (`loadFlights` +
   `submitResult`) for .NET `executeJavaScript` delegation.

### AG-UI streaming note (important)

`@ag-ui/client` `HttpAgent` + `MCPAppsMiddleware.run(...).subscribe(...)`
inside this Express handler returned HTTP 200 SSE headers but **emitted 0
events** to the client (empty body for tens of seconds), while the same
`HttpAgent` stream worked in a standalone Node probe and while direct
`POST :3011/ag-ui/...` streamed fine.

Root cause for clients: the BFF was not forwarding early AG-UI events.
Fix: raw SSE proxy. Tradeoff: server-side `ACTIVITY_SNAPSHOT` / `mcp-apps`
emission on pending hotel UI tool calls at `RUN_FINISHED` is no longer done
by middleware on the main agent path. UI tool **schemas** are still injected
(with timeout + in-memory cache in `mcp-ui-tools.ts`) so the LLM can request
them; iframe `__proxiedMCPRequest` still uses middleware. Point `MCP_URL` at
the TypeScript `mcp-server` (UI-annotated tools) for hotel widgets — the
.NET `ai-cs-mcp-server` currently exposes no `ui/resourceUri` tools.

AgentMode remapping (`plan` → `planningAgent`) stays on .NET
(`AgentModeRemapMiddleware`). The BFF forwards the path as-is
(`/ag-ui/ticketingAgent`).

Plan-mode `STATE_SNAPSHOT` (full travel plan) and `renderA2uiTool` /
`renderDashboard` `a2ui-surface` activity snapshots are emitted by
**ai-cs-server** `AGUIStreamOptions.MapResult` and pass through this BFF SSE
stream unchanged.

## Pending-tool strategy (critical)

With `UseMcpApps: true` (default), `ai-cs-server` removes `findHotels`
from `ticketingAgent` and `hotelAgent` so the LLM only sees the MCP UI tool
injected by this BFF as an AG-UI frontend tool. Local `findHotels` remains on
agents that still need a backend implementation (e.g. travelRefinement,
package workflow helpers).

After the raw SSE proxy change, the BFF still injects those UI tool schemas
(cached `listTools` filtered by `ui/resourceUri`). It no longer runs the
middleware `processStream` path that executed pending UI tools and emitted
`ACTIVITY_SNAPSHOT` `mcp-apps` at `RUN_FINISHED` on the main agent stream.
Hotel iframe flows that rely on `__proxiedMCPRequest` continue to work via
middleware. Restoring server-side `ACTIVITY_SNAPSHOT` on the main path would
require a streaming tee that forwards early events immediately and only
holds `RUN_FINISHED`.

## Environment

| Variable          | Default                                        |
| ----------------- | ---------------------------------------------- |
| `PORT`            | `3001`                                         |
| `DOTNET_AGUI_URL` | `http://127.0.0.1:3011`                        |
| `MCP_URL`         | `http://127.0.0.1:3002/mcp`                    |
| `FLIGHT_API_BASE` | `https://demo.angulararchitects.io/api/flight` |

## Run (three terminals)

From the repo root, in separate terminals:

```bash
pnpm mcp-server
pnpm ai-cs-server
pnpm ai-cs-bff
```

Or `.NET + BFF` together: `pnpm ai-cs`.

Then start Angular with `ng serve -o`.

## executeJavaScript delegation

.NET tool `executeJavaScript` (`ExecuteJavaScriptTools`) POSTs
`{ code, title }` to `BFF_EXECUTE_JS_URL` (default
`http://127.0.0.1:3001/internal/execute-javascript`).

BFF handler calls `executeJavaScript()` in `src/execute-javascript.ts`, which
runs `quickjs-emscripten` via `src/sandbox.ts` with host functions:

- `await loadFlights(from, to)` → `fetchFlights` (`src/fetch-flights.ts`)
- `submitResult(items)` → captured chart `{ name, value }[]`

Success response: `{ ok: true, data, code, title }`. On sandbox failure:
`{ ok: false, data: [], title, code: "", message }`.
