# Flights42 with AG-UI

## Table of Contents

- [Providing API Key and Selecting Model](#providing-api-key-and-selecting-model)
  - [Starting and Running the Example](#starting-and-running-the-example)
  - [Trying out](#trying-out)
- [.NET + BFF stack (ai-cs-\*)](#net--bff-stack-ai-cs-)
- [Mini-Applications](#mini-applications)
  - [AG-UI SDK Demo (Chapter 2)](#ag-ui-sdk-demo-chapter-2)
  - [Mastra + AG-UI Demo (Chapter 2)](#mastra--ag-ui-demo-chapter-2)
  - [.NET Agent Framework Demo (ai-cs-demo)](#net-agent-framework-demo-ai-cs-demo)
  - [A2UI Demo (Chapter 3)](#a2ui-demo-chapter-3)
  - [MCP Apps Demo (Chapter 4)](#mcp-apps-demo-chapter-4)

## Providing API Key and Selecting Model

For executing the example, you need an OpenAI API Key for GPT or a GOOGLE API Key for Gemini. Set it as an environment variable:

```bash
# Bash (MacOS, Linux, ...)
export OPENAI_API_KEY=...
```

```bash
# CMD (Windows)
set OPENAI_API_KEY=...
```

### Starting and Running the Example

After `pnpm install`, you can start the MCP Server

```bash
pnpm mcp-server
```

Start the Backend:

```bash
pnpm ai-server
```

In a further terminal, start the client:

```bash
ng serve -o
```

## .NET + BFF stack (ai-cs-\*)

Alternative stack: Angular talks to the public BFF entry on `:3001`, which fronts the internal .NET AG-UI host on `:3011` and wires MCP Apps + QuickJS.

```
Angular -> ai-cs-bff :3001 (public) -> ai-cs-server :3011 (internal)
             |-> mcp-server :3002 Streamable HTTP
             |-> QuickJS /internal/execute-javascript
```

Normal setup: browser/`ConfigService` use `:3001`; .NET listens on `:3011` behind the BFF. Plan tools emit full-plan `STATE_SNAPSHOT`; `renderA2uiTool` / `renderDashboard` emit `a2ui-surface` `ACTIVITY_SNAPSHOT` from the .NET host through the BFF.

Run (separate terminals, from the repo root), with **pnpm**:

```bash
pnpm mcp-server
pnpm ai-cs-server
pnpm ai-cs-bff
ng serve -o
```

Or start .NET + BFF together: `pnpm ai-cs`.

Preferred orchestrated path: `pnpm ai-cs-aspire` (AppHost + Dashboard). See `ai-cs-aspire/README.md`.

Defaults target **LM Studio** (`LLM_ENDPOINT`, `OPENAI_API_KEY=lm-studio`, `OPENAI_CHAT_MODEL` in `ai-cs-server/appsettings.json`). Override those if you use a cloud OpenAI-compatible endpoint.

More detail:

- [ai-cs-bff/README.md](ai-cs-bff/README.md)
- [ai-cs-server/README.md](ai-cs-server/README.md)
- [ai-cs-mcp-server/README.md](ai-cs-mcp-server/README.md) (optional .NET MCP alternate for `MCP_URL`)

### Trying out

1. In the app, switch to the `Booking`
2. Activate the Assistant (see button in bottom right corner)
3. Ask some questions

Ideas for questions:

- Did I already book for Paris?
- Show me hotel there
- Show me hotels in London

## Mini-Applications

Besides the flight application, the repository contains small stand-alone
demos. Each one isolates a single concept and can be started on its own.

### AG-UI SDK Demo (Chapter 2)

Plain AG-UI SDK without an agent framework and without a language model: the
agent hardcodes its AG-UI events, the client logs every received event.
Deliberately without HTTP — the client talks to the agent in-process, so the
focus stays on the messages:

```bash
pnpm ag-ui-simple:client
```

A second agent demonstrates client-side tools: it requests the client tool
`showWeather` in its first run and answers with text once the client has sent
back the tool result:

```bash
pnpm ag-ui-simple:client-tools
```

No API key and no server needed — the agents emit prepared events. HTTP and
server-sent events come into play with the Mastra demo below.

### Mastra + AG-UI Demo (Chapter 2)

A real Mastra agent with a weather tool behind an AG-UI endpoint, plus a
command-line client. Needs an API key:

```bash
pnpm ai-demo-server
pnpm ai-demo-client          # add -- --details to log every AG-UI event
```

The matching minimal Angular client with CopilotKit:

```bash
pnpm simple-client
```

### .NET Agent Framework Demo (ai-cs-demo)

Minimal Microsoft Agent Framework + AG-UI demo (server + CLI client), analogous
to the Mastra ai-demo:

```bash
pnpm ai-cs-demo-server
pnpm ai-cs-demo-client
```

### A2UI Demo (Chapter 3)

Renders hardcoded A2UI messages with the Angular renderer — no agent, no model:

```bash
ng serve a2ui-demo
```

### MCP Apps Demo (Chapter 4)

A VanillaJS host and app communicating over the MCP Apps protocol:

```bash
pnpm mcp-apps-demo
```
