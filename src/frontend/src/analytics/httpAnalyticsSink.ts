import type { JourneyAnalyticsEvent, JourneyAnalyticsSink } from './JourneyAnalyticsContext'
import { applicantAuthHeaders } from '../onboarding/api/applicant-session'

/**
 * Sends journey events to the API so they join the durable trail.
 *
 * Events are batched: a journey raises one on every step view and interaction, and a request each
 * would put the analytics pipeline in the critical path of the form the applicant is filling in.
 *
 * Delivery is strictly best-effort. A failed flush is dropped rather than retried forever, and no
 * failure is ever surfaced — analytics must not be able to break an onboarding journey.
 */
export interface HttpAnalyticsSinkOptions {
  /** Endpoint to post batches to. */
  url?: string
  /** Flush once this many events are queued. */
  batchSize?: number
  /** Flush this long after the first event in a batch, even if the batch is not full. */
  flushIntervalMs?: number
  /** Injectable for tests. */
  fetchImpl?: typeof fetch
}

const DEFAULT_URL = '/api/analytics/events'
const DEFAULT_BATCH_SIZE = 20
const DEFAULT_FLUSH_INTERVAL_MS = 5000

/** Matches the server's per-batch cap; a larger batch would be rejected wholesale. */
const MAX_BATCH_SIZE = 100

export interface HttpAnalyticsSink extends JourneyAnalyticsSink {
  /** Sends anything queued now. Safe to call when the queue is empty. */
  flush(): Promise<void>
  /** Stops the timer and flushes. */
  close(): Promise<void>
}

export function createHttpAnalyticsSink(options: HttpAnalyticsSinkOptions = {}): HttpAnalyticsSink {
  const url = options.url ?? DEFAULT_URL
  const batchSize = Math.min(options.batchSize ?? DEFAULT_BATCH_SIZE, MAX_BATCH_SIZE)
  const flushIntervalMs = options.flushIntervalMs ?? DEFAULT_FLUSH_INTERVAL_MS
  const fetchImpl = options.fetchImpl ?? ((...args: Parameters<typeof fetch>) => fetch(...args))

  let queue: JourneyAnalyticsEvent[] = []
  let timer: ReturnType<typeof setTimeout> | null = null

  function clearTimer() {
    if (timer !== null) {
      clearTimeout(timer)
      timer = null
    }
  }

  async function flush(): Promise<void> {
    clearTimer()
    if (queue.length === 0) return

    // Take the queue before awaiting so events raised during the request are not lost or resent.
    const batch = queue.slice(0, MAX_BATCH_SIZE)
    queue = queue.slice(MAX_BATCH_SIZE)

    const events = batch
      // An event with no session cannot be attributed, and the server would reject the batch.
      .filter((event) => event.sessionId !== null)
      .map((event) => ({
        eventId: event.eventId,
        eventType: event.eventType,
        journeyId: event.journeyId,
        sessionId: event.sessionId,
        stepId: event.stepId,
        stepIndex: event.stepIndex,
        payload: event.payload,
        occurredAt: event.occurredAt,
      }))

    if (events.length === 0) return

    try {
      await fetchImpl(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...applicantAuthHeaders() },
        body: JSON.stringify({ events }),
        keepalive: true,
      })
    } catch {
      // Dropped. Retrying risks an unbounded queue on a long-broken endpoint, and a lost
      // analytics event matters far less than a stalled application form.
    }

    if (queue.length > 0) scheduleFlush()
  }

  function scheduleFlush() {
    if (timer !== null) return
    timer = setTimeout(() => void flush(), flushIntervalMs)
  }

  return {
    name: 'http',

    track(event: JourneyAnalyticsEvent): void {
      queue.push(event)

      if (queue.length >= batchSize) {
        void flush()
        return
      }

      scheduleFlush()
    },

    flush,

    async close(): Promise<void> {
      clearTimer()
      await flush()
    },
  }
}
