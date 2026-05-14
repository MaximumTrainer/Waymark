import { createContext, useCallback, useContext, useMemo, useRef, type ReactNode } from 'react'
import { buildAnalyticsEvent, dispatchToSinks } from './analyticsHelpers'

// ── Event schema ──────────────────────────────────────────────────────────────

export type JourneyEventType =
  | 'step_view'
  | 'interaction'
  | 'validation_error'
  | 'navigation_next'
  | 'navigation_back'
  | 'journey_complete'

export interface JourneyAnalyticsEvent {
  /** Unique identifier for this event instance. */
  eventId: string
  /** The type of event. */
  eventType: JourneyEventType
  /** The journey (flow) id. */
  journeyId: string
  /** The active session id, if available. */
  sessionId: string | null
  /** The current step node id. */
  stepId: string | null
  /** Zero-based index of the step within the session. */
  stepIndex: number | null
  /** Dynamic data relevant to this event. */
  payload: Record<string, unknown>
  /** UTC timestamp when the event was captured. */
  occurredAt: string
}

// ── Sink (provider) interface ─────────────────────────────────────────────────

export interface JourneyAnalyticsSink {
  /** Unique name used for debugging / log output. */
  name: string
  /** Receives the event for delivery.  Must not throw. */
  track(event: JourneyAnalyticsEvent): void | Promise<void>
}

// ── Context ───────────────────────────────────────────────────────────────────

interface JourneyAnalyticsContextValue {
  journeyId: string | null
  sessionId: string | null
  /**
   * Track a journey analytics event.
   * @param eventType - the lifecycle event type
   * @param stepId - node id of the active step
   * @param stepIndex - zero-based step position within the session
   * @param payload - additional event-specific data (PII must be excluded by the caller)
   */
  track(
    eventType: JourneyEventType,
    stepId: string | null,
    stepIndex: number | null,
    payload?: Record<string, unknown>,
  ): void
  /** Register a new analytics sink at runtime. */
  registerSink(sink: JourneyAnalyticsSink): void
}

const JourneyAnalyticsContext = createContext<JourneyAnalyticsContextValue>({
  journeyId: null,
  sessionId: null,
  track: () => undefined,
  registerSink: () => undefined,
})

// ── Provider ──────────────────────────────────────────────────────────────────

export interface JourneyAnalyticsProviderProps {
  journeyId: string | null
  sessionId: string | null
  initialSinks?: JourneyAnalyticsSink[]
  children: ReactNode
}

export function JourneyAnalyticsProvider({
  journeyId,
  sessionId,
  initialSinks = [],
  children,
}: JourneyAnalyticsProviderProps) {
  const sinksRef = useRef<JourneyAnalyticsSink[]>(initialSinks)

  const track = useCallback(
    (
      eventType: JourneyEventType,
      stepId: string | null,
      stepIndex: number | null,
      payload: Record<string, unknown> = {},
    ) => {
      const event = buildAnalyticsEvent(
        eventType,
        journeyId ?? '',
        sessionId,
        stepId,
        stepIndex,
        payload,
      )
      dispatchToSinks(sinksRef.current, event)
    },
    [journeyId, sessionId],
  )

  const registerSink = useCallback((sink: JourneyAnalyticsSink) => {
    if (!sinksRef.current.some((s) => s.name === sink.name)) {
      sinksRef.current = [...sinksRef.current, sink]
    }
  }, [])

  const value = useMemo<JourneyAnalyticsContextValue>(
    () => ({ journeyId, sessionId, track, registerSink }),
    [journeyId, sessionId, track, registerSink],
  )

  return (
    <JourneyAnalyticsContext.Provider value={value}>
      {children}
    </JourneyAnalyticsContext.Provider>
  )
}

// ── Hook ──────────────────────────────────────────────────────────────────────

/**
 * Returns the analytics context for the current journey.
 * Must be used inside a <JourneyAnalyticsProvider>.
 */
// eslint-disable-next-line react-refresh/only-export-components
export function useJourneyAnalytics(): JourneyAnalyticsContextValue {
  return useContext(JourneyAnalyticsContext)
}
