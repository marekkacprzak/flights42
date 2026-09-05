# ai-cs-mcp-server

ASP.NET Core Streamable HTTP MCP Hotels server paralleling `mcp-server/` for `@ag-ui/mcp-apps-middleware` at `http://127.0.0.1:3002/mcp`.

**Optional alternate** to the TypeScript `mcp-server` when configuring BFF `MCP_URL` (same Streamable HTTP `/mcp` contract). Prefer `pnpm mcp-server` for day-to-day MCP Apps; use this project when you want the hotels MCP App hosted in .NET instead.

## Transport

- Streamable HTTP via ModelContextProtocol.AspNetCore `MapMcp("/mcp")`.
- Not WebSockets. Middleware expects HTTP `/mcp`.
- Stateless by default; `GET /mcp` returns 405 (no long-lived SSE), same intent as the TS server.

## Run

```bash
pnpm ai-cs-mcp-server
```

```bash
dotnet run --project ai-cs-mcp-server --urls http://127.0.0.1:3002
```

Point the BFF at it with `MCP_URL=http://127.0.0.1:3002/mcp` (default). Do not run TS `mcp-server` and this listener on the same port at once.

## Exposes

- Tool `findHotels` (Biz Hotel / Skyline Suites / Grand Palace + city suffix + shuffle)
- McpAppUi ResourceUri `ui://hotels/results.html` + `WithMcpApps()`
- Resource `ui://hotels/results.html` (`text/html;profile=mcp-app`), CSP `resourceDomains` `http://127.0.0.1:3002`
- Static `/assets/hotels/*`
- CORS Origin `*`, expose `mcp-session-id`, MCP headers, Private-Network

HTML from `mcp-server/dist/index.html` is in `wwwroot/ui/results.html`.

## Packages

ModelContextProtocol.AspNetCore 2.2.0, ModelContextProtocol.Extensions.Apps 2.2.0, net10.0.
