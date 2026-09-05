const FLIGHT_API_BASE =
  process.env.FLIGHT_API_BASE ?? 'https://demo.angulararchitects.io/api/flight';

interface RawFlight {
  id: number;
  from: string;
  to: string;
  date: string;
  delayed?: boolean;
  delay?: number;
}

export interface FlightRecord {
  id: number;
  from: string;
  to: string;
  date: string;
  delay: number;
}

function normaliseDelay(raw: RawFlight): number {
  if (typeof raw.delay === 'number') {
    return raw.delay;
  }
  return raw.delayed ? 15 : 0;
}

export async function fetchFlights(
  from: string,
  to: string,
): Promise<FlightRecord[]> {
  const params = new URLSearchParams({ from, to });
  const response = await fetch(`${FLIGHT_API_BASE}?${params.toString()}`, {
    headers: { Accept: 'application/json' },
  });

  if (!response.ok) {
    throw new Error(
      `searchFlights: backend responded with status ${response.status}`,
    );
  }

  const raw = (await response.json()) as RawFlight[];
  return raw.map((entry) => ({
    id: entry.id,
    from: entry.from,
    to: entry.to,
    date: entry.date,
    delay: normaliseDelay(entry),
  }));
}
