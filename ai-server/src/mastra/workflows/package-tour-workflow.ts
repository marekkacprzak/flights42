import { createStep, createWorkflow } from '@mastra/core/workflows';
import { z } from 'zod';

import { readBridge } from '../../../../libs/ag-ui-server/step-bridge.js';
import { hotelSchema } from '../tools/find-hotels.js';
import { flightSchema, searchFlights } from '../tools/search-flights.js';

const packageInputSchema = z.object({
  from: z.string().describe('Departure city (name, not IATA code).'),
  to: z.string().describe('Destination city (name, not IATA code).'),
  departDate: z
    .string()
    .describe('Planned outbound departure date (ISO 8601, e.g. 2026-05-15).'),
  returnDate: z
    .string()
    .describe('Planned return flight date (ISO 8601, e.g. 2026-05-22).'),
  minStars: z
    .number()
    .int()
    .min(1)
    .max(10)
    .describe(
      'Minimum required hotel star rating. Typical mapping: 3 = budget, 4 = standard, 5 = premium. Values above 5 (e.g. 6 for "Superluxus"/"VIP") intentionally cannot be satisfied by the hotel catalog.',
    ),
});

const flightListSchema = z.object({
  flights: z.array(flightSchema),
});

const hotelListSchema = z.object({
  city: z.string(),
  hotels: z.array(hotelSchema),
});

interface StepProgressContext {
  writer?: { write: (chunk: unknown) => Promise<void> };
  requestContext?: Parameters<typeof readBridge>[0];
  /**
   * Name of the surrounding workflow step. Forwarded onto every bridge tool
   * call so the client can group calls under their parent step.
   */
  stepName?: string;
}

async function emitStepStatus(
  ctx: StepProgressContext,
  stepName: string,
  status: 'started' | 'finished',
  extras?: Record<string, unknown>,
): Promise<void> {
  // Primary path: per-request bridge stored on RequestContext by the AG-UI
  // adapter. This bypasses Mastra's tool-stream pipe entirely and is reliably
  // delivered even when the workflow is invoked as a sub-tool of an agent.
  const bridge = readBridge(ctx.requestContext);
  bridge?.emit({ stepName, kind: status, details: extras });

  // Secondary path: also push a custom chunk into the workflow's stream so
  // any consumer that DOES receive the piped output sees the progress too.
  // The `data-` prefix is Mastra's reserved namespace for custom chunks.
  await ctx.writer?.write({
    type: 'data-step-status',
    stepName,
    status,
    ...(extras ?? {}),
  });
}

/**
 * Surface a synchronous "I just called X with these args, here's the result"
 * event to the AG-UI client as a regular tool call. Useful for exposing work
 * that happens inside a workflow step (direct service calls, sub-agent
 * invocations) so the UI's Tool-Calls list reflects the full picture.
 */
async function withToolCall<T>(
  ctx: StepProgressContext,
  toolName: string,
  args: unknown,
  run: () => Promise<T>,
): Promise<T> {
  const bridge = readBridge(ctx.requestContext);
  const result = await run();
  bridge?.emitToolCall({
    toolName,
    args,
    result,
    stepName: ctx.stepName,
  });
  return result;
}

function createFlightSearchStep(
  id: 'findOutboundFlights' | 'findReturnFlights',
  direction: 'outbound' | 'return',
) {
  return createStep({
    id,
    description:
      direction === 'outbound'
        ? 'Searches for flights from the origin city to the destination.'
        : 'Searches for flights from the destination back to the origin.',
    inputSchema: packageInputSchema,
    outputSchema: flightListSchema,
    execute: async ({ inputData, writer, requestContext }) => {
      const ctx = { writer, requestContext, stepName: id };
      await emitStepStatus(ctx, id, 'started');
      const [from, to] =
        direction === 'outbound'
          ? [inputData.from, inputData.to]
          : [inputData.to, inputData.from];
      const flights = await withToolCall(
        ctx,
        'searchFlights',
        { from, to },
        () => searchFlights(from, to),
      );
      await emitStepStatus(ctx, id, 'finished', { count: flights.length });
      return { flights };
    },
  });
}

const findOutboundFlightsStep = createFlightSearchStep(
  'findOutboundFlights',
  'outbound',
);

const findReturnFlightsStep = createFlightSearchStep(
  'findReturnFlights',
  'return',
);

const findHotelsStep = createStep({
  id: 'findHotels',
  description:
    'Asks the hotel agent for hotel options in the destination city.',
  inputSchema: packageInputSchema,
  outputSchema: hotelListSchema,
  execute: async ({ inputData, mastra, writer, requestContext }) => {
    const ctx = { writer, requestContext, stepName: 'findHotels' };
    await emitStepStatus(ctx, 'findHotels', 'started');
    const agent = mastra.getAgent('hotelAgent');
    const bridge = readBridge(requestContext);
    const result = await agent.generate(
      [
        {
          role: 'user',
          content: `Find hotels in ${inputData.to}.`,
        },
      ],
      {
        structuredOutput: {
          schema: hotelListSchema,
        },
      },
    );

    if (result.object) {
      // Emit a slim, UI-friendly view of the sub-agent call. Sending the full
      // `result` would also include messages/usage/finishReason and is too
      // noisy for the tool-call list.
      bridge?.emitToolCall({
        toolName: 'agent-hotelAgent',
        args: { city: inputData.to },
        result: result.object,
        stepName: ctx.stepName,
      });
      await emitStepStatus(ctx, 'findHotels', 'finished', {
        count: result.object.hotels.length,
      });
      return result.object;
    }

    throw new Error('Hotel agent did not return structured hotel data.');
  },
});

const afterParallelSchema = z.object({
  findOutboundFlights: flightListSchema,
  findReturnFlights: flightListSchema,
  findHotels: hotelListSchema,
});

const afterEvaluationSchema = afterParallelSchema.extend({
  hotelMatch: hotelSchema.nullable(),
});

const packageOutputSchema = afterEvaluationSchema;

const evaluateHotelsStep = createStep({
  id: 'evaluateHotels',
  description:
    "Picks the cheapest hotel whose star rating meets the user's minStars criterion, or null if none qualifies.",
  inputSchema: afterParallelSchema,
  outputSchema: afterEvaluationSchema,
  execute: async ({ inputData, getInitData, writer, requestContext }) => {
    const ctx = { writer, requestContext };
    await emitStepStatus(ctx, 'evaluateHotels', 'started');
    const init = getInitData<z.infer<typeof packageInputSchema>>();
    const candidates = [...inputData.findHotels.hotels].sort(
      (a, b) => a.sterne - b.sterne,
    );
    const match =
      candidates.find((hotel) => hotel.sterne >= init.minStars) ?? null;
    await emitStepStatus(ctx, 'evaluateHotels', 'finished', {
      matched: match !== null,
    });
    return { ...inputData, hotelMatch: match };
  },
});

const hotelMatchStateStep = createStep({
  id: 'hotelMatchState',
  description:
    'Terminal state: a hotel matching the criterion was found. Passes the accumulated result through unchanged.',
  inputSchema: afterEvaluationSchema,
  outputSchema: packageOutputSchema,
  execute: async ({ inputData, writer, requestContext }) => {
    const ctx = { writer, requestContext };
    await emitStepStatus(ctx, 'hotelMatchState', 'started');
    await emitStepStatus(ctx, 'hotelMatchState', 'finished');
    return inputData;
  },
});

const hotelFallbackStateStep = createStep({
  id: 'hotelFallbackState',
  description:
    'Terminal state: no hotel matches the criterion. The travel agency will arrange the hotel booking manually. Flights are still proposed.',
  inputSchema: afterEvaluationSchema,
  outputSchema: packageOutputSchema,
  execute: async ({ inputData, writer, requestContext }) => {
    const ctx = { writer, requestContext };
    await emitStepStatus(ctx, 'hotelFallbackState', 'started');
    await emitStepStatus(ctx, 'hotelFallbackState', 'finished');
    return inputData;
  },
});

const finalizeStep = createStep({
  id: 'finalize',
  description:
    'Collapses the branch result (only one of the two terminal states ran) into a single workflow output.',
  inputSchema: z.object({
    hotelMatchState: packageOutputSchema.optional(),
    hotelFallbackState: packageOutputSchema.optional(),
  }),
  outputSchema: packageOutputSchema,
  execute: async ({ inputData, writer, requestContext }) => {
    const ctx = { writer, requestContext };
    await emitStepStatus(ctx, 'finalize', 'started');
    const result = inputData.hotelMatchState ?? inputData.hotelFallbackState;
    if (!result) {
      throw new Error('Package tour workflow ended without a terminal state.');
    }
    await emitStepStatus(ctx, 'finalize', 'finished');
    return result;
  },
});

export const packageTourWorkflow = createWorkflow({
  id: 'packageTourWorkflow',
  description:
    'Proposes a package tour: searches outbound flights, return flights, and hotels in parallel, then branches based on whether a hotel matches the requested minimum star rating.',
  inputSchema: packageInputSchema,
  outputSchema: packageOutputSchema,
})
  .parallel([findOutboundFlightsStep, findReturnFlightsStep, findHotelsStep])
  .then(evaluateHotelsStep)
  .branch([
    [
      async ({ inputData }) => inputData.hotelMatch !== null,
      hotelMatchStateStep,
    ],
    [
      async ({ inputData }) => inputData.hotelMatch === null,
      hotelFallbackStateStep,
    ],
  ])
  .then(finalizeStep)
  .commit();
