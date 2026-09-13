import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createHttpAnalyticsSink } from './httpAnalyticsSink'
import type { JourneyAnalyticsEvent } from './JourneyAnalyticsContext'
import { clearApplicantToken, setApplicantToken } from '../onboarding/api/applicant-session'

function event(overrides: Partial<JourneyAnalyticsEvent> = {}): JourneyAnalyticsEvent {
  return {
    eventId: crypto.randomUUID(),
    eventType: 'step_view',
    journeyId: 'journey-1',
    sessionId: 'session-1',
    stepId: 'node-1',
    stepIndex: 0,
    payload: {},
    occurredAt: new Date().toISOString(),
    ...overrides,
  } as JourneyAnalyticsEvent
}

describe('http analytics sink', () => {
  let fetchImpl: ReturnType<typeof vi.fn>

  beforeEach(() => {
    vi.useFakeTimers()
    fetchImpl = vi.fn().mockResolvedValue(new Response('{}', { status: 202 }))
    setApplicantToken('token-abc')
  })

  afterEach(() => {
    vi.useRealTimers()
    clearApplicantToken()
  })

  function bodyOf(call: number): { events: Array<Record<string, unknown>> } {
    return JSON.parse(String(fetchImpl.mock.calls[call][1].body))
  }

  it('does not send a request per event', () => {
    const sink = createHttpAnalyticsSink({ fetchImpl, batchSize: 5 })

    sink.track(event())
    sink.track(event())

    expect(fetchImpl).not.toHaveBeenCalled()
  })

  it('sends once the batch is full', async () => {
    const sink = createHttpAnalyticsSink({ fetchImpl, batchSize: 3 })

    sink.track(event({ eventType: 'step_view' }))
    sink.track(event({ eventType: 'interaction' }))
    sink.track(event({ eventType: 'navigation_next' }))
    await vi.runAllTimersAsync()

    expect(fetchImpl).toHaveBeenCalledTimes(1)
    expect(bodyOf(0).events).toHaveLength(3)
  })

  it('sends a partial batch once the interval elapses', async () => {
    const sink = createHttpAnalyticsSink({ fetchImpl, batchSize: 10, flushIntervalMs: 1000 })

    sink.track(event())
    expect(fetchImpl).not.toHaveBeenCalled()

    await vi.advanceTimersByTimeAsync(1000)

    expect(fetchImpl).toHaveBeenCalledTimes(1)
    expect(bodyOf(0).events).toHaveLength(1)
  })

  it('authenticates with the applicant session token', async () => {
    const sink = createHttpAnalyticsSink({ fetchImpl, batchSize: 1 })

    sink.track(event())
    await vi.runAllTimersAsync()

    const headers = fetchImpl.mock.calls[0][1].headers as Record<string, string>
    expect(headers.Authorization).toBe('Bearer token-abc')
    expect(headers['X-Api-Key']).toBeUndefined()
  })

  it('drops events that have no session, since they cannot be attributed', async () => {
    const sink = createHttpAnalyticsSink({ fetchImpl, batchSize: 2 })

    sink.track(event({ sessionId: null }))
    sink.track(event({ sessionId: null }))
    await vi.runAllTimersAsync()

    expect(fetchImpl).not.toHaveBeenCalled()
  })

  it('never throws when the endpoint fails', async () => {
    fetchImpl.mockRejectedValue(new Error('network down'))
    const sink = createHttpAnalyticsSink({ fetchImpl, batchSize: 1 })

    expect(() => sink.track(event())).not.toThrow()
    await expect(vi.runAllTimersAsync()).resolves.not.toThrow()
    await expect(sink.flush()).resolves.toBeUndefined()
  })

  it('keeps accepting events after a failed flush', async () => {
    fetchImpl.mockRejectedValueOnce(new Error('network down'))
    const sink = createHttpAnalyticsSink({ fetchImpl, batchSize: 1 })

    sink.track(event({ eventType: 'step_view' }))
    await vi.runAllTimersAsync()

    sink.track(event({ eventType: 'journey_complete' }))
    await vi.runAllTimersAsync()

    expect(fetchImpl).toHaveBeenCalledTimes(2)
    expect(bodyOf(1).events[0].eventType).toBe('journey_complete')
  })

  it('does not resend events from a failed batch', async () => {
    // Retrying forever would grow the queue without bound on a long-broken endpoint.
    fetchImpl.mockRejectedValueOnce(new Error('network down'))
    const sink = createHttpAnalyticsSink({ fetchImpl, batchSize: 1 })

    sink.track(event({ eventType: 'validation_error' }))
    await vi.runAllTimersAsync()
    await sink.flush()

    expect(fetchImpl).toHaveBeenCalledTimes(1)
  })

  it('flush sends what is queued and is a no-op when empty', async () => {
    const sink = createHttpAnalyticsSink({ fetchImpl, batchSize: 50 })

    await sink.flush()
    expect(fetchImpl).not.toHaveBeenCalled()

    sink.track(event())
    await sink.flush()

    expect(fetchImpl).toHaveBeenCalledTimes(1)
  })

  it('caps a batch at the server limit', async () => {
    const sink = createHttpAnalyticsSink({ fetchImpl, batchSize: 500 })

    for (let i = 0; i < 120; i++) sink.track(event())
    await vi.runAllTimersAsync()

    expect(bodyOf(0).events.length).toBeLessThanOrEqual(100)
  })

  it('close flushes and stops the timer', async () => {
    const sink = createHttpAnalyticsSink({ fetchImpl, batchSize: 50, flushIntervalMs: 1000 })

    sink.track(event())
    await sink.close()

    expect(fetchImpl).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(5000)
    expect(fetchImpl).toHaveBeenCalledTimes(1)
  })
})
