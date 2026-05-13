import { useEffect, useState } from 'react'
import { FlowAuthoringPanel } from './builder/FlowAuthoringPanel'
import { JourneyBuilder } from './builder/JourneyBuilder'
import { StepRenderer } from './onboarding/components/StepRenderer'
import { useOnboarding } from './onboarding/hooks/useOnboarding'

type JourneyOption = {
  id: string
  label: string
  description: string
}

const JOURNEYS: JourneyOption[] = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    label: 'Journey 1 — Small business onboarding',
    description:
      'Simple flow collecting business name, address, revenue, owner details and mocked online document verification.',
  },
  {
    id: '22222222-2222-2222-2222-222222222222',
    label: 'Journey 2 — Medium business onboarding',
    description:
      'Medium flow with primary and secondary owners, outlets/staff size, document upload, and mocked Experian + Companies House checks.',
  },
  {
    id: '33333333-3333-3333-3333-333333333333',
    label: 'Journey 3 — Large nationwide business onboarding',
    description:
      'High-complexity flow with legal structure, advanced compliance questionnaire, outlets/staff size, and mocked Experian + Companies House checks.',
  },
]
const defaultFlowId = JOURNEYS[0].id

function App() {
  const { step, startSession, submitStep, isLoading, error } = useOnboarding()
  const apiKey = import.meta.env.VITE_API_KEY || undefined
  const [selectedFlowId, setSelectedFlowId] = useState<string>(JOURNEYS[0].id)
  const [visitedNodeIds, setVisitedNodeIds] = useState<Set<string>>(new Set())
  const [builderFlowId, setBuilderFlowId] = useState<string | null>(defaultFlowId)

  useEffect(() => {
    startSession({ flowId: selectedFlowId })
      .then((result) => {
        if (result.currentNode?.id) {
          setVisitedNodeIds(new Set([result.currentNode.id]))
        } else {
          setVisitedNodeIds(new Set())
        }
      })
      .catch(() => undefined)
  }, [selectedFlowId, startSession])

  const selectedJourney = JOURNEYS.find((j) => j.id === selectedFlowId) ?? JOURNEYS[0]

  return (
    <main className="mx-auto max-w-5xl space-y-6 p-6">
      <header className="space-y-2">
        <h1 className="text-2xl font-bold text-slate-900">Open Onboarding</h1>
        <p className="text-sm text-slate-600">Schema-driven onboarding UI with workflow branching and compliance checks.</p>
      </header>

      <section className="space-y-3 rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
        <h2 className="text-lg font-semibold text-slate-900">Choose onboarding journey</h2>
        <label htmlFor="journey-select" className="block text-sm font-medium text-slate-700">
          Journey
        </label>
        <select
          id="journey-select"
          value={selectedFlowId}
          onChange={(event) => {
            const nextFlowId = event.target.value
            setSelectedFlowId(nextFlowId)
            setBuilderFlowId(nextFlowId)
          }}
          className="w-full rounded border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-1 focus:ring-slate-500"
        >
          {JOURNEYS.map((journey) => (
            <option key={journey.id} value={journey.id}>
              {journey.label}
            </option>
          ))}
        </select>
        <p className="text-sm text-slate-600">{selectedJourney.description}</p>
      </section>

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
            sessionId={step.sessionId}
            nodeId={step.currentNode?.id}
            apiKey={apiKey}
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
