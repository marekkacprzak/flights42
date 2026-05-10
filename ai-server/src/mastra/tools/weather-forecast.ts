import { createTool } from '@mastra/core/tools';
import { z } from 'zod';

const conditions = ['Sunny', 'Partly cloudy', 'Cloudy', 'Rain', 'Thunder'];

function hashString(value: string): number {
  let hash = 0;
  for (let i = 0; i < value.length; i++) {
    hash = (hash * 31 + value.charCodeAt(i)) | 0;
  }
  return Math.abs(hash);
}

function pickCondition(seed: number): string {
  return conditions[seed % conditions.length];
}

function pickTemperature(seed: number): number {
  return (seed % 25) + 5 - 5;
}

const forecastRequestSchema = z.object({
  city: z.string().describe('City name, e.g. "Hamburg".'),
  date: z
    .string()
    .describe('ISO date or date-time. Only the YYYY-MM-DD part is used.'),
});

const forecastResultSchema = z.object({
  city: z.string(),
  date: z.string(),
  condition: z.string(),
  temperatureC: z.number(),
});

export const weatherForecastTool = createTool({
  id: 'weatherForecasts',
  description: [
    'Batched deterministic mocked weather forecast. Returns one forecast',
    'per requested `(city, date)` pair so the agent can enrich multiple',
    'tiles (e.g. one per booked flight) in a SINGLE call instead of one',
    'round-trip per city.',
    '',
    'Input: `{ forecasts: { city, date }[] }`. Always pass ALL forecasts',
    'you need for the current turn in one call — never call this tool',
    'more than once per turn.',
    '',
    'Output: `{ results: { city, date, condition, temperatureC }[] }` in',
    'the SAME ORDER as the input `forecasts` array. Date is normalised to',
    'the YYYY-MM-DD prefix.',
  ].join('\n'),
  inputSchema: z.object({
    forecasts: z
      .array(forecastRequestSchema)
      .min(1)
      .describe(
        'All weather forecasts to compute in this turn. Pass every (city, date) pair in ONE call.',
      ),
  }),
  outputSchema: z.object({
    results: z.array(forecastResultSchema),
  }),
  execute: async ({ forecasts }) => {
    const results = forecasts.map(({ city, date }) => {
      const day = date.slice(0, 10);
      const seed = hashString(`${city.toLowerCase()}|${day}`);
      return {
        city,
        date: day,
        condition: pickCondition(seed),
        temperatureC: pickTemperature(seed),
      };
    });
    return { results };
  },
});
