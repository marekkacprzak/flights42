import { runSandbox } from './sandbox.js';
import { fetchFlights } from './fetch-flights.js';

export interface DataItem {
  name: string;
  value: number;
}

export interface ExecuteJavaScriptResult {
  data: DataItem[];
  code: string;
  title: string;
}

export async function executeJavaScript(
  code: string,
  title: string,
): Promise<ExecuteJavaScriptResult> {
  let captured: DataItem[] = [];

  await runSandbox(code, {
    functions: {
      loadFlights: (from: string, to: string) => {
        return fetchFlights(from, to);
      },
      submitResult: (items: DataItem[]) => {
        captured = items;
      },
    },
  });

  return { data: captured, code, title };
}
