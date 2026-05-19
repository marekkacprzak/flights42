import { Agent } from '@mastra/core/agent';
import { Memory } from '@mastra/memory';

import {
  addCustomCatalogInstructions,
  renderA2uiTool,
} from '../../../../libs/ag-ui-server/index.js';
import { aggregateDataTool } from '../tools/aggregate-data.js';
import { findBookedFlightsTool } from '../tools/find-booked-flights.js';
import { renderChartTool } from '../tools/render-chart.js';
import { searchFlightsTool } from '../tools/search-flights.js';
import { searchHotelsTool } from '../tools/search-hotels.js';
import { searchRentalCarsTool } from '../tools/search-rental-cars.js';
import { weatherForecastTool } from '../tools/weather-forecast.js';
import { dashboardSlowAgentPrompt } from './dashboard-slow-agent.prompt.js';

// Dashboard agent variant used to measure the cost of LLM-driven tool
// chaining for charts. `renderFlightChartTool` is intentionally absent
// from the tool set, so the agent must chain
// `searchFlightsTool` → `aggregateDataTool` → `renderChartTool` for
// every chart tile.
//
// Same model and step budget as the fast `dashboardAgent` so duration
// differences come from the extra round-trips, not from a different
// model. Higher step ceiling because chart-heavy prompts can need many
// extra rounds (3 tools per chart × multiple charts plus the final
// renderA2uiTool).
export const dashboardSlowAgent = new Agent({
  id: 'dashboardSlowAgent',
  name: 'Flight42 Dashboard Composer (multi-step charts)',
  instructions: addCustomCatalogInstructions({
    systemInstructions: dashboardSlowAgentPrompt,
    log: false,
  }),
  model: 'openai/gpt-5.3-chat-latest',
  tools: {
    searchFlightsTool,
    aggregateDataTool,
    weatherForecastTool,
    findBookedFlightsTool,
    renderChartTool,
    searchRentalCarsTool,
    searchHotelsTool,
    renderA2uiTool,
  },
  defaultOptions: { maxSteps: 30 },
  memory: new Memory(),
});
