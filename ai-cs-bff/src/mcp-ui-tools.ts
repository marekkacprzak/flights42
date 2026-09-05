import type { Tool } from '@ag-ui/client';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js';

export interface McpServerConfig {
  type: 'http' | 'sse';
  url: string;
  serverId?: string;
}

interface CachedUiTool {
  tool: Tool;
  serverConfig: McpServerConfig;
  resourceUri: string;
}

interface CacheState {
  tools: CachedUiTool[];
  fetchedAt: number;
  inflight: Promise<CachedUiTool[]> | null;
}

const cacheByUrl = new Map<string, CacheState>();

const DEFAULT_TTL_MS = 60_000;
const DEFAULT_TIMEOUT_MS = 2_000;

function isUiTool(tool: { _meta?: Record<string, unknown> }): tool is {
  name: string;
  description?: string;
  inputSchema?: object;
  _meta: { 'ui/resourceUri': string };
} {
  return typeof tool._meta?.['ui/resourceUri'] === 'string';
}

function toAgUiTool(tool: {
  name: string;
  description?: string;
  inputSchema?: object;
  _meta: { 'ui/resourceUri': string };
}): Tool {
  const resourceUri = tool._meta['ui/resourceUri'];
  const description = tool.description || '';
  return {
    name: tool.name,
    description: `${description}\n[UI Resource: ${resourceUri}]`,
    parameters: tool.inputSchema || { type: 'object', properties: {} },
  };
}

async function fetchUiToolsFromServer(
  server: McpServerConfig,
): Promise<CachedUiTool[]> {
  const transport = new StreamableHTTPClientTransport(new URL(server.url));
  const client = new Client(
    { name: 'ai-cs-bff-mcp-cache', version: '1.0.0' },
    {
      capabilities: {
        extensions: {
          'io.modelcontextprotocol/ui': { mimeTypes: ['text/html+mcp'] },
        },
      },
    },
  );
  try {
    await client.connect(transport);
    const listed = await client.listTools();
    return listed.tools.filter(isUiTool).map((tool) => ({
      tool: toAgUiTool(tool),
      serverConfig: server,
      resourceUri: tool._meta['ui/resourceUri'],
    }));
  } finally {
    await client.close().catch(() => undefined);
  }
}

function withTimeout<T>(promise: Promise<T>, ms: number): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => {
      reject(new Error(`MCP UI tools timed out after ${ms}ms`));
    }, ms);
    promise.then(
      (value) => {
        clearTimeout(timer);
        resolve(value);
      },
      (err) => {
        clearTimeout(timer);
        reject(err);
      },
    );
  });
}

export async function getCachedMcpUiTools(
  servers: McpServerConfig[],
  options?: { ttlMs?: number; timeoutMs?: number },
): Promise<CachedUiTool[]> {
  if (servers.length === 0) {
    return [];
  }
  const ttlMs = options?.ttlMs ?? DEFAULT_TTL_MS;
  const timeoutMs = options?.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  const key = servers
    .map((s) => `${s.type}:${s.url}:${s.serverId ?? ''}`)
    .join('|');
  const now = Date.now();
  let state = cacheByUrl.get(key);
  if (state && now - state.fetchedAt < ttlMs) {
    return state.tools;
  }
  if (state?.inflight) {
    try {
      return await withTimeout(state.inflight, timeoutMs);
    } catch {
      return state.tools;
    }
  }
  if (!state) {
    state = { tools: [], fetchedAt: 0, inflight: null };
    cacheByUrl.set(key, state);
  }
  const inflight = (async () => {
    const collected: CachedUiTool[] = [];
    for (const server of servers) {
      try {
        const tools = await fetchUiToolsFromServer(server);
        collected.push(...tools);
      } catch (err) {
        console.error(
          `Failed to fetch MCP UI tools from ${server.url}:`,
          err instanceof Error ? err.message : err,
        );
      }
    }
    return collected;
  })();
  state.inflight = inflight;
  try {
    const tools = await withTimeout(inflight, timeoutMs);
    state.tools = tools;
    state.fetchedAt = Date.now();
    state.inflight = null;
    return tools;
  } catch (err) {
    state.inflight = null;
    console.error(
      'MCP UI tools fetch timed out or failed; using cache:',
      err instanceof Error ? err.message : err,
    );
    return state.tools;
  }
}

export function mcpUiToolsAsAgUiTools(cached: CachedUiTool[]): Tool[] {
  return cached.map((entry) => entry.tool);
}
