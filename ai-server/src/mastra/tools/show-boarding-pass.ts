import {
  A2UI_DEFAULT_CATALOG_ID,
  extractCatalogId,
} from '@internal/ag-ui-server';
import { createTool } from '@mastra/core/tools';
import { z } from 'zod';

import { fetchFlight } from '../data/booked-flights-store.js';

/** Fallback when the client did not forward a custom catalog — TicketWidget lives on the flights42 catalog. */
const FLIGHTS42_CATALOG_ID = 'https://example.com/catalogs/flights42-a2ui-demo';

const flightSchema = z.object({
  id: z.number(),
  from: z.string(),
  to: z.string(),
  date: z.string(),
  delay: z.number(),
});

export const SHOW_BOARDING_PASS_TOOL_NAME = 'showBoardingPass';

/**
 * Server-built TicketWidget surface. Prefer this over renderA2uiTool for
 * boarding-pass intents — local models often omit the required `messages`
 * argument and fail the call.
 *
 * Return shape `{ surfaceId, messages }` is detected by ExtendedMastraAgent
 * and forwarded as an `a2ui-surface` ACTIVITY_SNAPSHOT.
 */
export const showBoardingPassTool = createTool({
  id: SHOW_BOARDING_PASS_TOOL_NAME,
  description: `
    Show a boarding-pass TicketWidget (barcode/QR) for one flight.
    Use this when the user asks for a ticket / boarding pass / TicketWidget.
    Pass the flight id from findBookedFlights (or the id the user named).
    Do NOT use flightWidget or renderA2uiTool for boarding-pass intents —
    this tool builds the A2UI surface server-side.
  `,
  inputSchema: z.object({
    flightId: z.number().describe('Flight id to render as a boarding pass.'),
  }),
  outputSchema: z.object({
    ok: z.literal(true),
    surfaceId: z.string(),
    messages: z.array(z.record(z.string(), z.unknown())),
    flight: flightSchema,
  }),
  execute: async ({ flightId }, { requestContext }) => {
    const flight = await fetchFlight(flightId);
    if (!flight) {
      throw new Error(`Flight ${flightId} was not found.`);
    }

    const agUi = requestContext?.get?.('ag-ui') as
      | { context?: { description?: string; value?: string }[] }
      | undefined;
    const fromContext = extractCatalogId(agUi?.context);
    // TicketWidget lives only on the flights42 custom catalog — never use the
    // A2UI basic catalog id for this surface.
    const resolvedCatalogId =
      fromContext && fromContext !== A2UI_DEFAULT_CATALOG_ID
        ? fromContext
        : FLIGHTS42_CATALOG_ID;

    const surfaceId = `boarding-${flight.id}-${crypto.randomUUID()}`;
    const date =
      flight.date.length >= 10 ? flight.date.slice(0, 10) : flight.date;

    const messages = [
      {
        version: 'v0.9',
        createSurface: {
          surfaceId,
          catalogId: resolvedCatalogId,
        },
      },
      {
        version: 'v0.9',
        updateComponents: {
          surfaceId,
          components: [
            {
              id: 'root',
              component: 'Column',
              children: ['heading', 'ticket'],
            },
            {
              id: 'heading',
              component: 'Text',
              text: `Boarding pass — ${flight.from} → ${flight.to}`,
            },
            {
              id: 'ticket',
              component: 'TicketWidget',
              ticketId: { path: '/ticket/ticketId' },
              from: { path: '/ticket/from' },
              to: { path: '/ticket/to' },
              date: { path: '/ticket/date' },
              delay: { path: '/ticket/delay' },
            },
          ],
        },
      },
      {
        version: 'v0.9',
        updateDataModel: {
          surfaceId,
          path: '/ticket',
          value: {
            ticketId: flight.id,
            from: flight.from,
            to: flight.to,
            date,
            delay: flight.delay,
          },
        },
      },
    ];

    return {
      ok: true as const,
      surfaceId,
      messages,
      flight,
    };
  },
});
