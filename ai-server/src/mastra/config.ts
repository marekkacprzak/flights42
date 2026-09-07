import { createOpenAI } from '@ai-sdk/openai';
import type { MastraModelConfig } from '@mastra/core/llm';

import { lmStudioFetch } from './utils/lm-studio-fetch.js';

export const useLmStudio =
  (process.env.USE_LM_STUDIO ?? 'true').toLowerCase() !== 'false';

const lmStudio = createOpenAI({
  name: 'lmstudio',
  baseURL: process.env.LLM_ENDPOINT ?? 'http://192.168.8.199:1234/v1',
  apiKey: process.env.OPENAI_API_KEY ?? 'lm-studio',
  fetch: lmStudioFetch,
});

const lmStudioModelId = process.env.OPENAI_CHAT_MODEL ?? 'qwen/qwen3-vl-8b';

export const model: MastraModelConfig = useLmStudio
  ? lmStudio.chat(lmStudioModelId)
  : 'openai/gpt-5.4-mini';

export const modelAdvancedTasks: MastraModelConfig = useLmStudio
  ? lmStudio.chat(lmStudioModelId)
  : 'openai/gpt-5.5';

// export const model: MastraModelConfig = 'openai/gpt-5.4-mini';
// export const modelAdvancedTasks: MastraModelConfig = 'openai/gpt-5.5';
// export const model: MastraModelConfig = 'google/gemini-flash-latest';
// export const modelAdvancedTasks: MastraModelConfig = 'google/gemini-flash-latest';
