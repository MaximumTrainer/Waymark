import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { SessionEventTrail } from './SessionEventTrail'
import { formatGap } from './eventTrailFormat'
import type { AnalyticsTrailEvent } from './SessionEventTrail'

/**
 * The persisted trail is what an operator reads to find out why an applicant stalled. Three things
 * have to hold for it to be readable: the order has to be the order events happened, a client event
 * has to be distinguishable from a server one, and an empty panel must say which kind of empty it
 * is — a session with no client events is normal, and one whose events aged out of retention is not
 * the same as one that never had any.
 */

const SESSION_ID = '11111111-1111-1111-1111-111111111111'

function event(overrides: Partial<AnalyticsTrailEvent> & { eventId: string }): AnalyticsTrailEvent {
  return {
    eventType: 'step_view',
    journeyId: '22222222-2222-2222-2222-222222222222',
    sessionId: SESSION_ID,
    stepId: 'identity',
    stepIndex: 0,
    payload: {},
    occurredAt: '2026-09-13T10:00:00.000Z',
    source: 'server',
    ...overrides,
  }
}

function mockTrail(events: AnalyticsTrailEvent[]) {
  return vi.spyOn(globalThis, 'fetch').mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => events,
  } as Response)
}

describe('SessionEventTrail', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('renders the trail in occurrence order', async () => {
    // Deliberately out of order from the API, so the assertion is about the view, not the fixture.
    mockTrail([
      event({ eventId: 'c', eventType: 'journey_complete', occurredAt: '2026-09-13T10:02:00.000Z' }),
      event({ eventId: 'a', eventType: 'session_started', occurredAt: '2026-09-13T10:00:00.000Z' }),
      event({ eventId: 'b', eventType: 'step_submitted', occurredAt: '2026-09-13T10:01:00.000Z' }),
    ])

    render(<SessionEventTrail sessionId={SESSION_ID} />)

    await waitFor(() => expect(screen.getAllByTestId('trail-event')).toHaveLength(3))

    const rendered = screen.getAllByTestId('trail-event').map((node) => node.textContent)
    expect(rendered[0]).toContain('session_started')
    expect(rendered[1]).toContain('step_submitted')
    expect(rendered[2]).toContain('journey_complete')
  })

  it('shows event type, step, timestamp and the gap since the previous event', async () => {
    mockTrail([
      event({ eventId: 'a', eventType: 'session_started', occurredAt: '2026-09-13T10:00:00.000Z' }),
      event({
        eventId: 'b',
        eventType: 'step_submitted',
        stepId: 'address',
        stepIndex: 1,
        occurredAt: '2026-09-13T10:02:30.000Z',
      }),
    ])

    render(<SessionEventTrail sessionId={SESSION_ID} />)

    await waitFor(() => expect(screen.getAllByTestId('trail-event')).toHaveLength(2))

    const second = screen.getAllByTestId('trail-event')[1]
    expect(second.textContent).toContain('step_submitted')
    expect(second.textContent).toContain('address')
    expect(second.textContent).toContain('#1')
    // The stall signal: two and a half minutes on one step.
    expect(second.textContent).toContain('+2m 30s')
    expect(second.textContent).toContain(new Date('2026-09-13T10:02:30.000Z').toLocaleString())
  })

  it('distinguishes client events from server events', async () => {
    mockTrail([
      event({ eventId: 'a', source: 'server', occurredAt: '2026-09-13T10:00:00.000Z' }),
      event({ eventId: 'b', source: 'client', occurredAt: '2026-09-13T10:00:30.000Z' }),
    ])

    render(<SessionEventTrail sessionId={SESSION_ID} />)

    await waitFor(() => expect(screen.getAllByTestId('trail-event')).toHaveLength(2))

    const [first, second] = screen.getAllByTestId('trail-event')
    expect(first).toHaveAttribute('data-source', 'server')
    expect(second).toHaveAttribute('data-source', 'client')

    // Labelled, not only coloured: a reader should not have to know the schema to interpret a gap.
    expect(first.textContent).toContain('server')
    expect(second.textContent).toContain('client')
  })

  it('says a trail with no client events is not an error', async () => {
    mockTrail([
      event({ eventId: 'a', source: 'server' }),
      event({ eventId: 'b', source: 'server', occurredAt: '2026-09-13T10:00:10.000Z' }),
    ])

    render(<SessionEventTrail sessionId={SESSION_ID} />)

    await waitFor(() => expect(screen.getAllByTestId('trail-event')).toHaveLength(2))

    expect(screen.getByText(/Server events only/i)).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('does not show the server-only note when client events are present', async () => {
    mockTrail([
      event({ eventId: 'a', source: 'server' }),
      event({ eventId: 'b', source: 'client', occurredAt: '2026-09-13T10:00:10.000Z' }),
    ])

    render(<SessionEventTrail sessionId={SESSION_ID} />)

    await waitFor(() => expect(screen.getAllByTestId('trail-event')).toHaveLength(2))

    expect(screen.queryByText(/Server events only/i)).not.toBeInTheDocument()
  })

  it('explains an empty trail rather than rendering a blank panel', async () => {
    mockTrail([])

    render(<SessionEventTrail sessionId={SESSION_ID} />)

    await waitFor(() => expect(screen.getByText(/No events recorded/i)).toBeInTheDocument())

    // A session old enough to have aged out looks identical to one that never had events, so the
    // empty state has to name both possibilities rather than implying data was lost.
    expect(screen.getByText(/retention window/i)).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('reports a failed load as an error, not as an empty trail', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: false,
      status: 500,
      json: async () => ({}),
    } as Response)

    render(<SessionEventTrail sessionId={SESSION_ID} />)

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())
    expect(screen.queryByText(/No events recorded/i)).not.toBeInTheDocument()
  })

  it('requests the trail through the operator credential', async () => {
    const fetchSpy = mockTrail([])

    render(<SessionEventTrail sessionId={SESSION_ID} />)

    await waitFor(() => expect(fetchSpy).toHaveBeenCalled())

    const [url, init] = fetchSpy.mock.calls[0]
    expect(url).toBe(`/api/analytics/sessions/${SESSION_ID}/events`)
    // The admin session cookie, like every other operator view. Never an API key.
    expect((init as RequestInit).credentials).toBe('include')
  })
})

describe('formatGap', () => {
  it('shows no gap for the first event', () => {
    expect(formatGap(null, '2026-09-13T10:00:00.000Z')).toBe('—')
  })

  it('scales the unit to the size of the gap', () => {
    expect(formatGap('2026-09-13T10:00:00.000Z', '2026-09-13T10:00:00.400Z')).toBe('+400ms')
    expect(formatGap('2026-09-13T10:00:00.000Z', '2026-09-13T10:00:05.000Z')).toBe('+5.0s')
    expect(formatGap('2026-09-13T10:00:00.000Z', '2026-09-13T10:03:20.000Z')).toBe('+3m 20s')
    expect(formatGap('2026-09-13T10:00:00.000Z', '2026-09-13T12:30:00.000Z')).toBe('+2h 30m')
  })

  it('does not render a negative gap from clock skew', () => {
    // A client event timestamped by the applicant browser can land fractionally before the server
    // event it followed. Showing a negative gap would read as an ordering bug.
    expect(formatGap('2026-09-13T10:00:05.000Z', '2026-09-13T10:00:04.000Z')).toBe('—')
  })
})
