import { useEffect, useMemo, useState } from 'react'
import { JourneyAnalyticsProvider } from '../analytics/JourneyAnalyticsContext'
import { consoleAnalyticsSink } from '../analytics/consoleAnalyticsSink'
import { createHttpAnalyticsSink } from '../analytics/httpAnalyticsSink'
import { StepRenderer } from './components/StepRenderer'
import { useOnboarding } from './hooks/useOnboarding'
import { defaultFlowId } from '../journeys'

/**
 * The public onboarding experience: one journey, one step at a time.
 *
 * Deliberately holds nothing an operator would see. Flow authoring, session history, analytics and
 * webhook deliveries live in the operator console behind the SSO check; they used to render on this
 * page, exposing every applicant's submitted data to anyone who opened it.
 *
 * The journey comes from the link the applicant was sent (`/?flowId=...`), not from a picker.
 */
export function ApplicantJourneyPage({ search }: { search: string }) {
  const { step, startSession, submitStep, isLoading, error } = useOnboarding()
  const [visitedNodeIds, setVisitedNodeIds] = useState<Set<string>>(new Set())

  const flowId = new URLSearchParams(search).get('flowId') ?? defaultFlowId

  // One sink for the lifetime of the page so its batch queue survives re-renders. The console sink
  // stays alongside it for local debugging.
  const httpSink = useMemo(() => createHttpAnalyticsSink(), [])
  const analyticsSinks = useMemo(() => [consoleAnalyticsSink, httpSink], [httpSink])

  useEffect(() => {
    // A journey often ends by the applicant closing the tab, so flush what is queued before the
    // page goes away rather than losing the tail of every session.
    const flushOnHide = () => { void httpSink.flush() }
    window.addEventListener('pagehide', flushOnHide)

    return () => {
      window.removeEventListener('pagehide', flushOnHide)
      void httpSink.close()
    }
  }, [httpSink])

  useEffect(() => {
    startSession({ flowId })
      .then((result) => {
        setVisitedNodeIds(result.currentNode?.id ? new Set([result.currentNode.id]) : new Set())
      })
      .catch(() => undefined)
  }, [flowId, startSession])

  const totalVisited = visitedNodeIds.size

  return (
    <JourneyAnalyticsProvider
      journeyId={flowId}
      sessionId={step?.sessionId ?? null}
      initialSinks={analyticsSinks}
    >
      <main className="mx-auto max-w-2xl space-y-6 p-6">
        <header className="space-y-1">
          <h1 className="text-2xl font-bold text-slate-900">Welcome</h1>
          <p className="text-sm text-slate-600">
            Complete the steps below to finish your application.
          </p>
        </header>

        <section className="space-y-3">
          {error ? (
            <p role="alert" className="rounded bg-rose-50 p-3 text-sm text-rose-600">
              {error}
            </p>
          ) : null}
          {isLoading ? <p className="text-sm text-slate-500">Loading…</p> : null}
          {!isLoading && !error && !step ? (
            <p className="rounded border border-slate-200 bg-white p-4 text-sm text-slate-600">
              Your application has not started yet.
            </p>
          ) : null}

          {step?.isCompleted ? (
            <p className="rounded border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-800">
              Your application is complete. Thank you.
            </p>
          ) : null}

          {step && !step.isCompleted ? (
            <>
              <p className="text-xs text-slate-500" aria-live="polite">
                Step {totalVisited}
              </p>
              <StepRenderer
                node={step.currentNode}
                sessionId={step.sessionId}
                nodeId={step.currentNode?.id}
                onSubmit={async (payload) => {
                  if (!step.currentNode) return
                  const nodeId = step.currentNode.id
                  setVisitedNodeIds((prev) => new Set([...prev, nodeId]))
                  await submitStep(step.sessionId, nodeId, { payload })
                }}
              />
            </>
          ) : null}
        </section>
      </main>
    </JourneyAnalyticsProvider>
  )
}
