import { useState } from 'react'
import { FlowAuthoringPanel } from '../builder/FlowAuthoringPanel'
import { JourneyBuilder } from '../builder/JourneyBuilder'
import {
  buildVersionToPersonaMap,
  upsertPersonaAssignment,
  type PersonaAssignment,
} from '../builder/personaRouting'
import { FlowAnalytics } from '../analytics/FlowAnalytics'
import { FlowVersionHistory } from '../flows/FlowVersionHistory'
import { SessionDetail } from '../sessions/SessionDetail'
import { SessionList, type SessionSummary } from '../sessions/SessionList'
import { WebhookDeliveries } from '../webhooks/WebhookDeliveries'
import { JOURNEYS, PERSONAS, defaultFlowId } from '../journeys'

/**
 * Operator console: flow authoring, version history, analytics, session history and webhook
 * deliveries.
 *
 * Every section here reads operator-only data — session lists include applicants' submitted
 * fields — so the whole page sits behind the Operator check in App. It used to render below the
 * step renderer on the public applicant page.
 */
export function AdminConsolePage() {
  const [selectedFlowId, setSelectedFlowId] = useState<string>(defaultFlowId)
  const [selectedPersona, setSelectedPersona] = useState<string>(PERSONAS[0].key)
  const [personaAssignments, setPersonaAssignments] = useState<PersonaAssignment[]>([])
  const [builderFlowId, setBuilderFlowId] = useState<string | null>(defaultFlowId)
  const [latestSelectedVersion, setLatestSelectedVersion] = useState<number | null>(null)
  const [selectedSession, setSelectedSession] = useState<SessionSummary | null>(null)

  const selectedJourney = JOURNEYS.find((j) => j.id === selectedFlowId) ?? JOURNEYS[0]
  const selectedPersonaLabel =
    PERSONAS.find((persona) => persona.key === selectedPersona)?.label ?? selectedPersona
  const activePersonasByVersion = buildVersionToPersonaMap(
    personaAssignments.filter((a) => a.flowId === builderFlowId),
  )

  return (
    <main className="mx-auto max-w-5xl space-y-6 p-6">
      <header className="space-y-2">
        <h1 className="text-2xl font-bold text-slate-900">Waymark Operator Console</h1>
        <p className="text-sm text-slate-600">
          Author journeys, review sessions, and inspect webhook deliveries.
        </p>
        <a href="/admin/journey-builder" className="inline-block text-sm font-medium text-indigo-700 underline">
          Open the visual Journey Builder →
        </a>
      </header>

      <section className="space-y-4 rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
        <h2 className="text-lg font-semibold text-slate-900">Journey Dashboard</h2>
        <div className="grid gap-3 md:grid-cols-2">
          <label htmlFor="journey-select" className="block text-sm font-medium text-slate-700">
            Journey
            <select
              id="journey-select"
              value={selectedFlowId}
              onChange={(event) => {
                const nextFlowId = event.target.value
                setSelectedFlowId(nextFlowId)
                setBuilderFlowId(nextFlowId)
                setLatestSelectedVersion(null)
              }}
              className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-1 focus:ring-slate-500"
            >
              {JOURNEYS.map((journey) => (
                <option key={journey.id} value={journey.id}>
                  {journey.label}
                </option>
              ))}
            </select>
          </label>
          <label htmlFor="persona-select" className="block text-sm font-medium text-slate-700">
            Persona
            <select
              id="persona-select"
              value={selectedPersona}
              onChange={(event) => setSelectedPersona(event.target.value)}
              className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-1 focus:ring-slate-500"
            >
              {PERSONAS.map((persona) => (
                <option key={persona.key} value={persona.key}>
                  {persona.label}
                </option>
              ))}
            </select>
          </label>
        </div>
        <p className="text-sm text-slate-600">{selectedJourney.description}</p>
        <div className="space-y-2 rounded border border-indigo-100 bg-indigo-50 p-3 text-sm text-indigo-900">
          <p>
            Preview this journey as an applicant:{' '}
            <a className="font-mono underline" href={`/?flowId=${selectedFlowId}`}>
              /?flowId={selectedFlowId}
            </a>
          </p>
          <p>
            Selected persona: <span className="font-semibold">{selectedPersonaLabel}</span>
          </p>
          <button
            type="button"
            onClick={() => {
              const flowId = builderFlowId ?? selectedFlowId
              setPersonaAssignments((prev) =>
                upsertPersonaAssignment(prev, {
                  personaKey: selectedPersona,
                  flowId,
                  liveVersion: latestSelectedVersion,
                }),
              )
            }}
            className="rounded border border-indigo-300 bg-white px-3 py-2 text-xs font-medium text-indigo-700 hover:bg-indigo-100"
          >
            Assign selected persona to builder flow
          </button>
        </div>
        {personaAssignments.length > 0 && (
          <ul className="space-y-1 text-xs text-slate-600">
            {personaAssignments.map((assignment) => (
              <li key={assignment.personaKey}>
                {assignment.personaKey} → {assignment.flowId} (live v{assignment.liveVersion ?? 'n/a'})
              </li>
            ))}
          </ul>
        )}
      </section>

      <FlowAuthoringPanel
        onFlowSelected={(flowId, version) => {
          setBuilderFlowId(flowId)
          setLatestSelectedVersion(version)
        }}
      />

      {builderFlowId && (
        <FlowVersionHistory
          flowId={builderFlowId}
          activePersonasByVersion={activePersonasByVersion}
          onRestore={() => undefined}
        />
      )}

      <section className="space-y-3">
        <h2 className="text-lg font-semibold text-slate-900">Journey Map</h2>
        <JourneyBuilder flowId={builderFlowId} visitedNodeIds={new Set()} isCompleted={false} />
      </section>

      <section className="space-y-3 rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
        <h2 className="text-lg font-semibold text-slate-900">Flow Analytics</h2>
        <FlowAnalytics flowId={selectedFlowId} />
      </section>

      <section className="space-y-3 rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
        <h2 className="text-lg font-semibold text-slate-900">Session History</h2>
        {selectedSession ? (
          <SessionDetail sessionId={selectedSession.id} onBack={() => setSelectedSession(null)} />
        ) : (
          <SessionList onSelectSession={setSelectedSession} />
        )}
      </section>

      <section className="space-y-3 rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
        <h2 className="text-lg font-semibold text-slate-900">Webhook Deliveries</h2>
        <WebhookDeliveries />
      </section>
    </main>
  )
}
