import http from 'node:http';

import { HttpAgent, type RunAgentInput, transformChunks } from '@ag-ui/client';
import { MCPAppsMiddleware } from '@ag-ui/mcp-apps-middleware';
import cors from 'cors';
import express, { type Request, type Response } from 'express';

import { executeJavaScript } from './execute-javascript.js';
import {
  getCachedMcpUiTools,
  type McpServerConfig,
  mcpUiToolsAsAgUiTools,
} from './mcp-ui-tools.js';

const PORT = Number(process.env.PORT ?? 3001);
const DOTNET_AGUI_URL = (
  process.env.DOTNET_AGUI_URL ?? 'http://127.0.0.1:3011'
).replace(/\/$/, '');
const MCP_URL = process.env.MCP_URL ?? 'http://127.0.0.1:3002/mcp';

const mcpServers: McpServerConfig[] = [
  {
    type: 'http',
    url: MCP_URL,
    serverId: 'hotels',
  },
];

const mcpApps = new MCPAppsMiddleware({
  mcpServers,
});

const app = express();
app.use(cors({ origin: '*' }));
app.use(express.json({ limit: '4mb' }));

app.use((req, res, next) => {
  const started = Date.now();
  console.log(`[ai-cs-bff] → ${req.method} ${req.originalUrl}`);
  res.on('finish', () => {
    console.log(
      `[ai-cs-bff] ← ${req.method} ${req.originalUrl} ${res.statusCode} ${Date.now() - started}ms`,
    );
  });
  res.on('close', () => {
    if (!res.writableEnded) {
      console.log(
        `[ai-cs-bff] ✕ ${req.method} ${req.originalUrl} aborted ${Date.now() - started}ms`,
      );
    }
  });
  next();
});

function isRunAgentInput(body: unknown): body is RunAgentInput {
  if (!body || typeof body !== 'object') {
    return false;
  }
  const input = body as Record<string, unknown>;
  return (
    typeof input.threadId === 'string' &&
    typeof input.runId === 'string' &&
    Array.isArray(input.messages)
  );
}

function writeSseHeaders(res: Response) {
  res.status(200);
  res.setHeader('Content-Type', 'text/event-stream; charset=utf-8');
  res.setHeader('Cache-Control', 'no-cache, no-transform');
  res.setHeader('Connection', 'keep-alive');
  res.setHeader('X-Accel-Buffering', 'no');
  if (
    typeof (res as Response & { flushHeaders?: () => void }).flushHeaders ===
    'function'
  ) {
    (res as Response & { flushHeaders: () => void }).flushHeaders();
  }
}

function subscribeAgentEvents(
  events$: ReturnType<MCPAppsMiddleware['run']>,
  req: Request,
  res: Response,
) {
  let writeQueue: Promise<void> = Promise.resolve();
  let closed = false;

  req.on('close', () => {
    closed = true;
  });

  const subscription = events$.pipe(transformChunks(false)).subscribe({
    next(event) {
      if (closed) {
        return;
      }
      writeQueue = writeQueue
        .then(() => {
          if (closed) {
            return;
          }
          res.write(`data: ${JSON.stringify(event)}\n\n`);
        })
        .catch(() => undefined);
    },
    error(err) {
      writeQueue = writeQueue
        .then(() => {
          if (!closed) {
            const message = err instanceof Error ? err.message : String(err);
            res.write(
              `data: ${JSON.stringify({
                type: 'RUN_ERROR',
                message,
              })}\n\n`,
            );
            res.end();
          }
        })
        .catch(() => undefined);
    },
    complete() {
      writeQueue = writeQueue
        .then(() => {
          if (!closed) {
            res.end();
          }
        })
        .catch(() => undefined);
    },
  });

  req.on('close', () => {
    subscription.unsubscribe();
  });
}

function proxyAgUiSse(
  req: Request,
  res: Response,
  agentId: string,
  input: RunAgentInput,
) {
  writeSseHeaders(res);

  const target = new URL(`${DOTNET_AGUI_URL}/ag-ui/${agentId}`);
  const payload = JSON.stringify(input);
  let closed = false;

  const upstream = http.request(
    target,
    {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Accept: 'text/event-stream',
        'Content-Length': Buffer.byteLength(payload),
      },
    },
    (upstreamRes) => {
      if ((upstreamRes.statusCode ?? 500) >= 400) {
        const chunks: Buffer[] = [];
        upstreamRes.on('data', (chunk) => {
          chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
        });
        upstreamRes.on('end', () => {
          if (closed) {
            return;
          }
          const message =
            Buffer.concat(chunks).toString('utf8') || 'upstream error';
          res.write(
            `data: ${JSON.stringify({
              type: 'RUN_ERROR',
              message,
            })}\n\n`,
          );
          res.end();
        });
        return;
      }

      upstreamRes.on('data', (chunk) => {
        if (closed) {
          return;
        }
        res.write(chunk);
      });
      upstreamRes.on('end', () => {
        if (!closed) {
          res.end();
        }
      });
      upstreamRes.on('error', (err) => {
        if (closed) {
          return;
        }
        res.write(
          `data: ${JSON.stringify({
            type: 'RUN_ERROR',
            message: err.message,
          })}\n\n`,
        );
        res.end();
      });
    },
  );

  upstream.on('error', (err) => {
    if (closed) {
      return;
    }
    if (!res.headersSent) {
      writeSseHeaders(res);
    }
    res.write(
      `data: ${JSON.stringify({
        type: 'RUN_ERROR',
        message: err.message,
      })}\n\n`,
    );
    res.end();
  });

  req.on('close', () => {
    closed = true;
    upstream.destroy();
  });

  upstream.end(payload);
}

app.post('/ag-ui/:agentId', async (req: Request, res: Response) => {
  const agentId = req.params.agentId;
  if (!agentId) {
    res.status(400).json({
      error: 'invalid_request',
      message: 'Missing agentId',
    });
    return;
  }

  if (!isRunAgentInput(req.body)) {
    res.status(400).json({
      error: 'invalid_request',
      message: 'Missing threadId, runId, or messages',
    });
    return;
  }

  const input: RunAgentInput = {
    ...req.body,
    tools: Array.isArray(req.body.tools) ? [...req.body.tools] : [],
  };

  console.log(
    `[ai-cs-bff] ag-ui ${agentId} thread=${input.threadId} run=${input.runId} messages=${input.messages.length} proxiedMcp=${Boolean(
      (input.forwardedProps as { __proxiedMCPRequest?: unknown } | undefined)
        ?.__proxiedMCPRequest,
    )}`,
  );

  const proxied = (
    input.forwardedProps as { __proxiedMCPRequest?: unknown } | undefined
  )?.__proxiedMCPRequest;
  if (proxied) {
    writeSseHeaders(res);
    const agent = new HttpAgent({
      url: `${DOTNET_AGUI_URL}/ag-ui/${agentId}`,
    });
    subscribeAgentEvents(mcpApps.run(input, agent), req, res);
    return;
  }

  try {
    const cached = await getCachedMcpUiTools(mcpServers);
    const uiTools = mcpUiToolsAsAgUiTools(cached);
    if (uiTools.length > 0) {
      const existing = new Set(input.tools.map((tool) => tool.name));
      for (const tool of uiTools) {
        if (!existing.has(tool.name)) {
          input.tools.push(tool);
        }
      }
    }
  } catch (err) {
    console.error(
      'MCP UI tool injection skipped:',
      err instanceof Error ? err.message : err,
    );
  }

  proxyAgUiSse(req, res, agentId, input);
});

app.post(
  '/internal/execute-javascript',
  async (req: Request, res: Response) => {
    const body = req.body as { code?: unknown; title?: unknown };
    const code = typeof body.code === 'string' ? body.code : '';
    const title = typeof body.title === 'string' ? body.title : '';
    console.log(
      `[ai-cs-bff] execute-javascript title=${JSON.stringify(title)} codeChars=${code.length}`,
    );

    if (!code) {
      res.status(400).json({
        ok: false,
        data: [],
        title,
        code: '',
        message: 'Missing code',
      });
      return;
    }

    try {
      const result = await executeJavaScript(code, title);
      res.json(result);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      res.status(500).json({
        ok: false,
        data: [],
        title,
        code: '',
        message,
      });
    }
  },
);

function proxyToDotnet(req: Request, res: Response) {
  const target = new URL(req.originalUrl, `${DOTNET_AGUI_URL}/`);
  const headers: http.OutgoingHttpHeaders = { ...req.headers };
  delete headers.host;
  delete headers['content-length'];

  const upstream = http.request(
    target,
    {
      method: req.method,
      headers,
    },
    (upstreamRes) => {
      res.status(upstreamRes.statusCode ?? 502);
      for (const [key, value] of Object.entries(upstreamRes.headers)) {
        if (value !== undefined) {
          res.setHeader(key, value);
        }
      }
      upstreamRes.pipe(res);
    },
  );

  upstream.on('error', (err) => {
    if (!res.headersSent) {
      res.status(502).json({
        error: 'bad_gateway',
        message: err.message,
      });
    } else {
      res.end();
    }
  });

  if (
    req.method === 'GET' ||
    req.method === 'HEAD' ||
    req.method === 'DELETE'
  ) {
    upstream.end();
    return;
  }

  if (req.body !== undefined && Object.keys(req.body as object).length > 0) {
    const payload = JSON.stringify(req.body);
    upstream.setHeader('Content-Type', 'application/json');
    upstream.setHeader('Content-Length', Buffer.byteLength(payload));
    upstream.end(payload);
    return;
  }

  req.pipe(upstream);
}

app.use('/bookings', proxyToDotnet);
app.use('/charts', proxyToDotnet);
app.use('/images', proxyToDotnet);
app.use('/dashboard', proxyToDotnet);
app.get('/health', proxyToDotnet);

app.listen(PORT, () => {
  console.log(
    `ai-cs-bff listening on http://127.0.0.1:${PORT} → ${DOTNET_AGUI_URL} (MCP ${MCP_URL}, raw SSE proxy)`,
  );
});
