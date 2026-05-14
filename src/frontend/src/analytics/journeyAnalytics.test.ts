import { describe, it, expect, vi } from 'vitest'
import { buildAnalyticsEvent, dispatchToSinks } from './analyticsHelpers'
import { consoleAnalyticsSink } from './consoleAnalyticsSink'
import {
  JourneyAnalyticsProvider,
  useJourneyAnalytics,
  type JourneyAnalyticsSink,
  type JourneyAnalyticsEvent,
} from './JourneyAnalyticsContext'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'

describe('buildAnalyticsEvent', () => {
  it('sets all required fields', () => {
    const event = buildAnalyticsEvent('step_view', 'journey-1', 'session-1', 'node-1', 0)

    expect(event.eventType).toBe('step_view')
    expect(event.journeyId).toBe('journey-1')
    expect(event.sessionId).toBe('session-1')
    expect(event.stepId).toBe('node-1')
    expect(event.stepIndex).toBe(0)
    expect(event.payload).toEqual({})
  })

  it('includes a non-empty eventId', () => {
    const event = buildAnalyticsEvent('navigation_next', 'j', 's', 'n', 1)
    expect(event.eventId).toBeTruthy()
    expect(typeof event.eventId).toBe('string')
  })

  it('includes a valid ISO occurredAt timestamp', () => {
    const before = Date.now()
    const event = buildAnalyticsEvent('journey_complete', 'j', null, null, null)
    const after = Date.now()

    const ts = new Date(event.occurredAt).getTime()
    expect(ts).toBeGreaterThanOrEqual(before)
    expect(ts).toBeLessThanOrEqual(after)
  })

  it('generates a unique eventId for each call', () => {
    const a = buildAnalyticsEvent('step_view', 'j', 's', 'n', 0)
    const b = buildAnalyticsEvent('step_view', 'j', 's', 'n', 0)
    expect(a.eventId).not.toBe(b.eventId)
  })

  it('includes the payload when provided', () => {
    const event = buildAnalyticsEvent('validation_error', 'j', 's', 'n', 0, {
      fieldName: 'Email',
      ruleId: 'required',
    })
    expect(event.payload).toEqual({ fieldName: 'Email', ruleId: 'required' })
  })

  it('allows null sessionId and stepId', () => {
    const event = buildAnalyticsEvent('journey_complete', 'j', null, null, null)
    expect(event.sessionId).toBeNull()
    expect(event.stepId).toBeNull()
    expect(event.stepIndex).toBeNull()
  })
})

describe('dispatchToSinks', () => {
  const makeEvent = (): JourneyAnalyticsEvent =>
    buildAnalyticsEvent('step_view', 'j-1', 's-1', 'n-1', 0)

  it('calls track on every registered sink', () => {
    const trackA = vi.fn()
    const trackB = vi.fn()
    const sinkA: JourneyAnalyticsSink = { name: 'a', track: trackA }
    const sinkB: JourneyAnalyticsSink = { name: 'b', track: trackB }

    const event = makeEvent()
    dispatchToSinks([sinkA, sinkB], event)

    expect(trackA).toHaveBeenCalledOnce()
    expect(trackA).toHaveBeenCalledWith(event)
    expect(trackB).toHaveBeenCalledOnce()
  })

  it('does not throw when a sink throws', () => {
    const faultySink: JourneyAnalyticsSink = {
      name: 'faulty',
      track() { throw new Error('boom') },
    }
    expect(() => dispatchToSinks([faultySink], makeEvent())).not.toThrow()
  })

  it('continues dispatching to remaining sinks after one throws', () => {
    const trackHealthy = vi.fn()
    const faultySink: JourneyAnalyticsSink = { name: 'faulty', track() { throw new Error('boom') } }
    const healthySink: JourneyAnalyticsSink = { name: 'healthy', track: trackHealthy }

    dispatchToSinks([faultySink, healthySink], makeEvent())

    expect(trackHealthy).toHaveBeenCalledOnce()
  })

  it('does nothing when the sink list is empty', () => {
    expect(() => dispatchToSinks([], makeEvent())).not.toThrow()
  })

  it('does not leak rejected promises from async sinks', async () => {
    const asyncFaultySink: JourneyAnalyticsSink = {
      name: 'async-faulty',
      track: async () => Promise.reject(new Error('async boom')),
    }

    expect(() => dispatchToSinks([asyncFaultySink], makeEvent())).not.toThrow()
    await Promise.resolve()
  })
})

describe('consoleAnalyticsSink', () => {
  it('logs to console.log with the [JourneyAnalytics] prefix', () => {
    const spy = vi.spyOn(console, 'log').mockImplementation(() => undefined)
    const event = buildAnalyticsEvent('step_view', 'j', 's', 'n', 0)
    consoleAnalyticsSink.track(event)

    expect(spy).toHaveBeenCalledOnce()
    const [prefix, eventType] = spy.mock.calls[0] as [string, string]
    expect(prefix).toBe('[JourneyAnalytics]')
    expect(eventType).toBe('step_view')

    spy.mockRestore()
  })
})

describe('JourneyAnalyticsProvider', () => {
  it('does not emit events when journeyId is missing', () => {
    const track = vi.fn()
    const sink: JourneyAnalyticsSink = {
      name: 'spy',
      track,
    }

    function TrackOnRender() {
      const analytics = useJourneyAnalytics()
      analytics.track('step_view', 'node-1', 0)
      return null
    }

    renderToStaticMarkup(
      createElement(JourneyAnalyticsProvider, {
        journeyId: null,
        sessionId: 'session-1',
        initialSinks: [sink],
        children: createElement(TrackOnRender),
      }),
    )

    expect(track).not.toHaveBeenCalled()
  })
})
