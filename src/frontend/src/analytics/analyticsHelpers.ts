import type { JourneyAnalyticsEvent, JourneyAnalyticsSink, JourneyEventType } from './JourneyAnalyticsContext'

/**
 * Builds a journey analytics event from the supplied parameters.
 * Extracted as a pure function so it can be unit-tested independently of React.
 */
export function buildAnalyticsEvent(
  eventType: JourneyEventType,
  journeyId: string,
  sessionId: string | null,
  stepId: string | null,
  stepIndex: number | null,
  payload: Record<string, unknown> = {},
): JourneyAnalyticsEvent {
  return {
    eventId: crypto.randomUUID(),
    eventType,
    journeyId,
    sessionId,
    stepId,
    stepIndex,
    payload,
    occurredAt: new Date().toISOString(),
  }
}

/**
 * Dispatches an event to all provided sinks.
 * Errors thrown by individual sinks are silently swallowed to protect the journey engine.
 */
export function dispatchToSinks(
  sinks: readonly JourneyAnalyticsSink[],
  event: JourneyAnalyticsEvent,
): void {
  for (const sink of sinks) {
    try {
      Promise.resolve(sink.track(event)).catch(() => {
        // Sinks must not disrupt the journey engine
      })
    } catch {
      // Sinks must not disrupt the journey engine
    }
  }
}
