import { useEffect, useState } from 'react'
import { FlowAuthoringPanel } from './builder/FlowAuthoringPanel'
import { JourneyBuilder } from './builder/JourneyBuilder'
import { StepRenderer } from './onboarding/components/StepRenderer'
import { useOnboarding } from './onboarding/hooks/useOnboarding'

const defaultFlowId = '11111111-1111-1111-1111-111111111111'

function App() {
  const { step, startSession, submitStep, isLoading, error } = useOnboarding()
  const [visitedNodeIds, setVisitedNodeIds] = useState<Set<string>>(new Set())
  const [builderFlowId, setBuilderFlowId] = useState<string | null>(defaultFlowId)

  useEffect(() => {
    startSession({ flowId: defaultFlowId })
      .then((result) => {
        if (result.currentNode?.id) {
          setVisitedNodeIds(new Set([result.currentNode.id]))
        }
      })
      .catch(() => undefined)
  }, [startSession])

  return (
    <main className="mx-auto max-w-5xl space-y-6 p-6">
      <header className="space-y-2">
        <h1 className="text-2xl font-bold text-slate-900">Open Onboarding</h1>
        <p className="text-sm text-slate-600">Schema-driven onboarding UI with workflow branching and compliance checks.</p>
      </header>

      <FlowAuthoringPanel onFlowSelected={setBuilderFlowId} />

      <section className="space-y-3">
        <h2 className="text-lg font-semibold text-slate-900">Visual Journey Builder (React Flow)</h2>
        <JourneyBuilder
          flowId={builderFlowId}
          currentNodeId={step?.currentNode?.id}
          visitedNodeIds={visitedNodeIds}
          isCompleted={step?.isCompleted ?? false}
        />
      </section>

      <section className="space-y-3">
        <h2 className="text-lg font-semibold text-slate-900">Dynamic UI Engine (Step Renderer)</h2>
        {error ? <p className="rounded bg-rose-50 p-3 text-sm text-rose-600">{error}</p> : null}
        {isLoading ? <p className="text-sm text-slate-500">Loading session…</p> : null}
        {!isLoading && !error && !step ? (
          <p className="rounded border border-slate-200 bg-white p-4 text-sm text-slate-600">Session has not started yet.</p>
        ) : null}
        {step ? (
          <StepRenderer
            node={step.currentNode}
            onSubmit={async (payload) => {
              if (!step.currentNode) {
                return
              }
              const nodeId = step.currentNode.id
              setVisitedNodeIds((prev) => new Set([...prev, nodeId]))
              await submitStep(step.sessionId, nodeId, { payload })
            }}
          />
        ) : null}
      </section>
    </main>
  )
}

export default App
