type JsonRecord = Record<string, unknown>;

function isRecord(value: unknown): value is JsonRecord {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function stripReasoning(node: unknown): boolean {
  if (!isRecord(node)) {
    return false;
  }

  const choices = node.choices;
  if (!Array.isArray(choices)) {
    return false;
  }

  let changed = false;
  for (const choiceNode of choices) {
    if (!isRecord(choiceNode)) {
      continue;
    }

    if (isRecord(choiceNode.delta) && 'reasoning_content' in choiceNode.delta) {
      delete choiceNode.delta.reasoning_content;
      changed = true;
    }

    if (
      isRecord(choiceNode.message) &&
      'reasoning_content' in choiceNode.message
    ) {
      delete choiceNode.message.reasoning_content;
      changed = true;
    }
  }

  return changed;
}

export function shouldDropChunk(node: unknown): boolean {
  if (!isRecord(node)) {
    return false;
  }

  const choices = node.choices;
  if (!Array.isArray(choices) || choices.length === 0) {
    return true;
  }

  const choice = choices[0];
  if (!isRecord(choice) || !isRecord(choice.delta)) {
    return false;
  }

  if (choice.finish_reason !== undefined && choice.finish_reason !== null) {
    return false;
  }

  const delta = choice.delta;
  for (const key of Object.keys(delta)) {
    if (
      key === 'role' ||
      key === 'content' ||
      key === 'tool_calls' ||
      key === 'function_call' ||
      key === 'refusal'
    ) {
      return false;
    }
  }

  return true;
}

export function stripReasoningFromJson(json: string): string {
  try {
    const node: unknown = JSON.parse(json);
    stripReasoning(node);
    return JSON.stringify(node);
  } catch {
    return json;
  }
}

function transformSseLine(line: string): string | null {
  if (!line.startsWith('data:')) {
    return line;
  }

  const payload = line.slice(5).trimStart();
  if (payload.length === 0 || payload === '[DONE]') {
    return line;
  }

  try {
    const node: unknown = JSON.parse(payload);
    stripReasoning(node);
    if (shouldDropChunk(node)) {
      return null;
    }

    return `data: ${JSON.stringify(node)}`;
  } catch {
    return line;
  }
}

function createReasoningStrippingSseStream(
  source: ReadableStream<Uint8Array>,
): ReadableStream<Uint8Array> {
  const decoder = new TextDecoder();
  const encoder = new TextEncoder();
  let buffer = '';

  return source.pipeThrough(
    new TransformStream<Uint8Array, Uint8Array>({
      transform(chunk, controller) {
        buffer += decoder.decode(chunk, { stream: true });
        const parts = buffer.split('\n');
        buffer = parts.pop() ?? '';

        for (const part of parts) {
          const line = part.endsWith('\r') ? part.slice(0, -1) : part;
          const transformed = transformSseLine(line);
          if (transformed === null) {
            continue;
          }

          controller.enqueue(encoder.encode(transformed + '\n'));
        }
      },
      flush(controller) {
        buffer += decoder.decode();
        if (buffer.length === 0) {
          return;
        }

        const line = buffer.endsWith('\r') ? buffer.slice(0, -1) : buffer;
        const transformed = transformSseLine(line);
        if (transformed !== null) {
          controller.enqueue(encoder.encode(transformed + '\n'));
        }
      },
    }),
  );
}

export async function lmStudioFetch(
  input: RequestInfo | URL,
  init?: RequestInit,
): Promise<Response> {
  const response = await fetch(input, init);
  const mediaType = response.headers
    .get('content-type')
    ?.split(';')[0]
    ?.trim()
    .toLowerCase();

  if (mediaType === 'text/event-stream' && response.body) {
    return new Response(createReasoningStrippingSseStream(response.body), {
      status: response.status,
      statusText: response.statusText,
      headers: response.headers,
    });
  }

  if (mediaType === 'application/json') {
    const body = await response.text();
    return new Response(stripReasoningFromJson(body), {
      status: response.status,
      statusText: response.statusText,
      headers: response.headers,
    });
  }

  return response;
}
