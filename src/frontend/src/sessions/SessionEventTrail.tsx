import { useEffect, useMemo, useState } from 'react'
import { operatorFetch } from '../api/operator-api'
import { formatGap, isClient, payloadSummary } from './eventTrailFormat'

/**
 * One session's persisted analytics trail.
 *
 * The submissions list next to this shows what an applicant entered. The trail shows how they got
 * there: the order steps were seen in, where time went, which validations failed. That is what an
 * operator investigating a stalled application actually needs, and until now it could only be had
 * by querying the API by hand.
 *
 * Operator-only, through `operatorFetch` and the admin session cookie, like every other operator
 * view. The applicant page never requests it.
 */
export interface AnalyticsTrailEvent {
  eventId: string
  eventType: string
  journeyId: string
  sessionId: string
  stepId?: string | null
  stepIndex?: number | null
  payload?: Record<string, unknown> | null
  occurredAt: string
  /** `server` for events the engine emits, `client` for events a browser reported. */
  source: string
}

interface SessionEventTrailProps {
  sessionId: string
}

export function SessionEventTrail({ sessionId }: SessionEventTrailProps) {
  const [events, setEvents] = useState<AnalyticsTrailEvent[]>([])
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()

    const load = async () => {
      setIsLoading(true)
      setError(null)
      try {
        const res = await operatorFetch(
          `/api/analytics/sessions/${sessionId}/events`,
          { signal: controller.signal },
        )
        if (!res.ok) throw new Error(`Failed to load the event trail (${res.status})`)
        setEvents((await res.json()) as AnalyticsTrailEvent[])
      } catch (err) {
        if (err instanceof Error && err.name !== 'AbortError') {
          setError(err.message)
        }
      } finally {
        setIsLoading(false)
      }
    }

    void load()
    return () => controller.abort()
  }, [sessionId])

  // The API returns the trail in order; sorting here keeps the view correct if that ever changes,
  // and a trail read out of order is worse than useless.
  const ordered = useMemo(
    () => [...events].sort((a, b) => new Date(a.occurredAt).getTime() - new Date(b.occurredAt).getTime()),
    [events],
  )

  const clientEventCount = ordered.filter((event) => isClient(event.source)).length

  return (
    <section className="space-y-3" aria-labelledby="event-trail-heading">
      <div className="flex items-baseline justify-between gap-3">
        <h4 id="event-trail-heading" className="text-sm font-semibold text-slate-800">
          Event trail
        </h4>
        {ordered.length > 0 && (
          <p className="text-xs text-slate-500">
            {ordered.length} event{ordered.length === 1 ? '' : 's'}
          </p>
        )}
      </div>

      {error && <p role="alert" className="rounded bg-rose-50 p-3 text-sm text-rose-600">{error}</p>}
      {isLoading && <p className="text-sm text-slate-500">Loading event trail…</p>}

      {!isLoading && !error && ordered.length === 0 && (
        <div className="rounded border border-slate-200 bg-slate-50 p-3 text-sm text-slate-600">
          <p className="font-medium text-slate-700">No events recorded for this session.</p>
          <p className="mt-1 text-xs text-slate-500">
            Either none were raised, or they have passed the analytics retention window and been
            deleted. Older sessions show nothing here even when the journey was completed normally.
          </p>
        </div>
      )}

      {!isLoading && !error && ordered.length > 0 && clientEventCount === 0 && (
        <p className="rounded border border-slate-200 bg-white p-2 text-xs text-slate-500">
          Server events only. Client events are reported by the applicant&apos;s browser and can be
          absent entirely — an ad blocker or a closed tab stops them reaching the API. This is not an
          error.
        </p>
      )}

      {ordered.length > 0 && (
        <ol className="space-y-2">
          {ordered.map((event, index) => {
            const gap = formatGap(index === 0 ? null : ordered[index - 1].occurredAt, event.occurredAt)
            const client = isClient(event.source)
            const summary = payloadSummary(event.payload)

            return (
              <li
                key={event.eventId}
                data-testid="trail-event"
                data-source={client ? 'client' : 'server'}
                className={
                  client
                    ? 'rounded-lg border border-dashed border-sky-300 bg-sky-50 p-3'
                    : 'rounded-lg border border-slate-200 bg-white p-3 shadow-sm'
                }
              >
                <div className="flex flex-wrap items-baseline gap-2">
                  <span className="font-mono text-sm font-semibold text-slate-800">{event.eventType}</span>
                  <span
                    className={
                      client
                        ? 'rounded bg-sky-200 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-sky-900'
                        : 'rounded bg-slate-200 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-slate-700'
                    }
                  >
                    {client ? 'client' : 'server'}
                  </span>
                  <span className="ml-auto font-mono text-xs text-slate-500">{gap}</span>
                </div>

                <p className="mt-1 text-xs text-slate-500">
                  <span>{new Date(event.occurredAt).toLocaleString()}</span>
                  <span className="mx-1.5">·</span>
                  <span>
                    Step:{' '}
                    {event.stepId
                      ? <span className="font-mono">{event.stepId}</span>
                      : <span className="italic text-slate-400">none</span>}
                    {typeof event.stepIndex === 'number' ? ` (#${event.stepIndex})` : ''}
                  </span>
                </p>

                {summary && (
                  <p className="mt-1 break-all font-mono text-xs text-slate-600">{summary}</p>
                )}
              </li>
            )
          })}
        </ol>
      )}
    </section>
  )
}
