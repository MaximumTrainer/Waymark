import { useCallback, useEffect, useState } from 'react'
import { FlowAuthoringPanel } from './builder/FlowAuthoringPanel'
import { JourneyBuilder } from './builder/JourneyBuilder'
import { buildVersionToPersonaMap, resolveFlowIdForPersona, upsertPersonaAssignment, type PersonaAssignment } from './builder/personaRouting'
import { StepRenderer } from './onboarding/components/StepRenderer'
import { useFlow } from './onboarding/hooks/useFlow'
import { useOnboarding } from './onboarding/hooks/useOnboarding'
import { FlowAnalytics } from './analytics/FlowAnalytics'
import { JourneyAnalyticsProvider } from './analytics/JourneyAnalyticsContext'
import { consoleAnalyticsSink } from './analytics/consoleAnalyticsSink'
import { WebhookDeliveries } from './webhooks/WebhookDeliveries'
import { SessionList } from './sessions/SessionList'
import { SessionDetail } from './sessions/SessionDetail'
import type { SessionSummary } from './sessions/SessionList'
import { FlowVersionHistory } from './flows/FlowVersionHistory'

type JourneyOption = {
  id: string
  label: string
  description: string
}

type PersonaOption = {
  key: string
  label: string
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
  {
    id: 'a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1',
    label: 'Journey A — Linear basic (test journey)',
    description:
      'Two-step linear journey: contact details form followed by a confirmation screen. Used for Playwright E2E test verification.',
  },
  {
    id: 'b2b2b2b2-b2b2-b2b2-b2b2-b2b2b2b2b2b2',
    label: 'Journey B — Conditional branch (test journey)',
    description:
      'Demonstrates conditional routing: EU applicants (France, Germany) see a GDPR disclosure; others see global terms.',
  },
  {
    id: 'c3c3c3c3-c3c3-c3c3-c3c3-c3c3c3c3c3c3',
    label: 'Journey C — Compliance heavy (test journey)',
    description:
      'Strict compliance rules, national ID pattern validation, document upload, and redirect to external verification service.',
  },
]
const defaultFlowId = JOURNEYS[0].id
const PERSONAS: PersonaOption[] = [
  { key: 'new-user', label: 'New User' },
  { key: 'enterprise-admin', label: 'Enterprise Admin' },
  { key: 'legacy-migratee', label: 'Legacy Migratee' },
]

type AuthMeResponse = {
  authenticated: boolean
  roles?: string[]
}

type AdminAuthState = 'checking' | 'authorized' | 'unauthorized'

const LOGIN_ERROR_MESSAGES: Record<string, string> = {
  saml_access_denied: 'Access Denied: User not authorized in IdP.',
  saml_certificate_expired: 'SAML certificate expired.',
  saml_invalid_assertion: 'Invalid SAML assertion.',
  saml_assertion_not_encrypted: 'SAML assertion was not encrypted.',
  saml_csrf_failed: 'SAML security validation failed. Please try again.',
  auth_check_failed: 'Could not validate your admin session. Please try again.',
}

const apiBase = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

function buildApiUrl(path: string) {
  return `${apiBase}${path}`
}

function App() {
  const { step, startSession, submitStep, isLoading, error } = useOnboarding()
  const apiKey = import.meta.env.VITE_API_KEY || undefined
  const [currentPath, setCurrentPath] = useState(() => window.location.pathname)
  const [currentSearch, setCurrentSearch] = useState(() => window.location.search)
  const [adminAuthState, setAdminAuthState] = useState<AdminAuthState>('checking')
  const [selectedFlowId, setSelectedFlowId] = useState<string>(JOURNEYS[0].id)
  const [selectedPersona, setSelectedPersona] = useState<string>(PERSONAS[0].key)
  const [personaAssignments, setPersonaAssignments] = useState<PersonaAssignment[]>([])
  const [visitedNodeIds, setVisitedNodeIds] = useState<Set<string>>(new Set())
  const [builderFlowId, setBuilderFlowId] = useState<string | null>(defaultFlowId)
  const [latestSelectedVersion, setLatestSelectedVersion] = useState<number | null>(null)
  const [selectedSession, setSelectedSession] = useState<SessionSummary | null>(null)
  const { flow: builderFlow } = useFlow(builderFlowId)

  const effectiveFlowId = resolveFlowIdForPersona(personaAssignments, selectedPersona, selectedFlowId)
  const activePersonasByVersion = buildVersionToPersonaMap(
    personaAssignments.filter((a) => a.flowId === builderFlowId),
  )

  useEffect(() => {
    if (currentPath === '/login') {
      return
    }

    if (currentPath.startsWith('/admin/journey-builder') && adminAuthState !== 'authorized') {
      return
    }

    startSession({ flowId: effectiveFlowId })
      .then((result) => {
        if (result.currentNode?.id) {
          setVisitedNodeIds(new Set([result.currentNode.id]))
        } else {
          setVisitedNodeIds(new Set())
        }
      })
      .catch(() => undefined)
  }, [adminAuthState, currentPath, effectiveFlowId, startSession])

  const selectedJourney = JOURNEYS.find((j) => j.id === selectedFlowId) ?? JOURNEYS[0]
  const assignedFlowIdForPersona = resolveFlowIdForPersona(personaAssignments, selectedPersona, selectedFlowId)
  const selectedPersonaLabel = PERSONAS.find((persona) => persona.key === selectedPersona)?.label ?? selectedPersona
  const isAdminJourneyBuilderRoute = currentPath.startsWith('/admin/journey-builder')
  const isLoginRoute = currentPath === '/login'

  useEffect(() => {
    const handlePopState = () => {
      setCurrentPath(window.location.pathname)
      setCurrentSearch(window.location.search)
    }

    window.addEventListener('popstate', handlePopState)
    return () => window.removeEventListener('popstate', handlePopState)
  }, [])

  const navigate = useCallback((path: string, replace = false) => {
    if (replace) {
      window.history.replaceState({}, '', path)
    } else {
      window.history.pushState({}, '', path)
    }
    if (path.startsWith('/admin/journey-builder')) {
      setAdminAuthState('checking')
    }
    setCurrentPath(window.location.pathname)
    setCurrentSearch(window.location.search)
  }, [])

  useEffect(() => {
    if (!isAdminJourneyBuilderRoute) return

    fetch(buildApiUrl('/api/auth/me'), { credentials: 'include' })
      .then(async (response) => {
        if (!response.ok) return { authenticated: false } satisfies AuthMeResponse
        return (await response.json()) as AuthMeResponse
      })
      .then((identity) => {
        const isAuthorized = identity.authenticated && (identity.roles ?? []).includes('Operator')
        if (!isAuthorized) {
          setAdminAuthState('unauthorized')
          navigate(`/login?returnUrl=${encodeURIComponent(currentPath)}`, true)
          return
        }

        setAdminAuthState('authorized')
      })
      .catch(() => {
        setAdminAuthState('unauthorized')
        navigate(`/login?error=auth_check_failed&returnUrl=${encodeURIComponent(currentPath)}`, true)
      })
  }, [currentPath, isAdminJourneyBuilderRoute, navigate])

  if (isLoginRoute) {
    const params = new URLSearchParams(currentSearch)
    const errorCode = params.get('error')
    const returnUrl = params.get('returnUrl') ?? '/admin/journey-builder'
    const message = errorCode ? (LOGIN_ERROR_MESSAGES[errorCode] ?? 'Authentication failed.') : null
    const loginUrl = `${buildApiUrl('/auth/saml/login')}?${new URLSearchParams({ returnUrl }).toString()}`

    return (
      <main className="mx-auto flex min-h-screen max-w-xl items-center p-6">
        <section className="w-full space-y-4 rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
          <h1 className="text-2xl font-bold text-slate-900">Admin Login</h1>
          <p className="text-sm text-slate-600">Sign in to access the Journey Builder admin experience.</p>
          {message ? <p role="alert" className="rounded border border-rose-200 bg-rose-50 p-3 text-sm text-rose-700">{message}</p> : null}
          <button
            type="button"
            onClick={() => window.location.assign(loginUrl)}
            className="rounded bg-slate-900 px-4 py-2 text-sm font-medium text-white"
          >
            Login with SSO
          </button>
        </section>
      </main>
    )
  }

  if (isAdminJourneyBuilderRoute && adminAuthState !== 'authorized') {
    return (
      <main className="mx-auto max-w-xl p-6">
        <p className="text-sm text-slate-600">Checking admin session…</p>
      </main>
    )
  }

  return (
    <JourneyAnalyticsProvider
      journeyId={effectiveFlowId}
      sessionId={step?.sessionId ?? null}
      initialSinks={[consoleAnalyticsSink]}
    >
    <main className="mx-auto max-w-5xl space-y-6 p-6">
      <header className="space-y-2">
        <h1 className="text-2xl font-bold text-slate-900">Open Onboarding</h1>
        <p className="text-sm text-slate-600">Schema-driven onboarding UI with workflow branching and compliance checks.</p>
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
            Active persona route: <span className="font-semibold">{selectedPersonaLabel}</span> →{' '}
            <span className="font-mono">{assignedFlowIdForPersona}</span>
          </p>
          <button
            type="button"
            onClick={() => {
              const flowId = builderFlowId ?? selectedFlowId
              setPersonaAssignments((prev) =>
                upsertPersonaAssignment(prev, {
                  personaKey: selectedPersona,
                  flowId,
                  liveVersion: latestSelectedVersion ?? builderFlow?.version ?? null,
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
          apiKey={apiKey}
          activePersonasByVersion={activePersonasByVersion}
          onRestore={() => undefined}
        />
      )}

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
      <section className="space-y-3 rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
        <h2 className="text-lg font-semibold text-slate-900">Flow Analytics</h2>
        <FlowAnalytics flowId={selectedFlowId} apiKey={apiKey} />
      </section>

      <section className="space-y-3 rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
        <h2 className="text-lg font-semibold text-slate-900">Session History</h2>
        {selectedSession ? (
          <SessionDetail
            sessionId={selectedSession.id}
            apiKey={apiKey}
            onBack={() => setSelectedSession(null)}
          />
        ) : (
          <SessionList apiKey={apiKey} onSelectSession={setSelectedSession} />
        )}
      </section>

      <section className="space-y-3 rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
        <h2 className="text-lg font-semibold text-slate-900">Webhook Deliveries</h2>
        <WebhookDeliveries apiKey={apiKey} />
      </section>
    </main>
    </JourneyAnalyticsProvider>
  )
}

export default App
