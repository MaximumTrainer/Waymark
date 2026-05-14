import { useMemo, useState } from 'react'
import type { FlowDefinition } from '../onboarding/types/flow'
import {
  areDraftGraphsEqual,
  buildFlowWritePayload,
  createDefaultFlowDraft,
  toFlowDraft,
  validateFlowDraft,
  type FlowDraft,
} from './flowAuthoring'

const serverBase = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')
const apiKey = import.meta.env.VITE_API_KEY

type FlowAuthoringPanelProps = {
  onFlowSelected: (flowId: string | null, version?: number | null) => void
}

type StatusState = {
  kind: 'idle' | 'success' | 'error'
  message: string
}

function buildHeaders(includeJsonContentType: boolean): Record<string, string> {
  const headers: Record<string, string> = {}
  if (includeJsonContentType) headers['Content-Type'] = 'application/json'
  if (apiKey) headers['X-Api-Key'] = apiKey
  return headers
}

async function readErrorMessage(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as { title?: string; errors?: Record<string, string[]> }
    if (problem.errors) {
      const first = Object.values(problem.errors)[0]?.[0]
      if (first) return first
    }
    if (problem.title) return problem.title
  } catch {
    // Ignore parse failures
  }
  return `Request failed (${response.status})`
}

function toEditorState(draft: FlowDraft) {
  return {
    name: draft.name,
    description: draft.description ?? '',
    nodesJson: JSON.stringify(draft.nodes, null, 2),
    connectionsJson: JSON.stringify(draft.connections, null, 2),
  }
}

export function FlowAuthoringPanel({ onFlowSelected }: FlowAuthoringPanelProps) {
  const [flowIdInput, setFlowIdInput] = useState('')
  const [name, setName] = useState(createDefaultFlowDraft().name)
  const [description, setDescription] = useState(createDefaultFlowDraft().description ?? '')
  const [nodesJson, setNodesJson] = useState(JSON.stringify(createDefaultFlowDraft().nodes, null, 2))
  const [connectionsJson, setConnectionsJson] = useState(JSON.stringify(createDefaultFlowDraft().connections, null, 2))
  const [currentVersion, setCurrentVersion] = useState<number | null>(null)
  const [lifecycleState, setLifecycleState] = useState<'Draft' | 'Published'>('Draft')
  const [personaKeys, setPersonaKeys] = useState('')
  const [status, setStatus] = useState<StatusState>({ kind: 'idle', message: '' })
  const [isBusy, setIsBusy] = useState(false)

  const statusClassName = useMemo(() => {
    if (status.kind === 'success') {
      return 'rounded border border-emerald-200 bg-emerald-50 p-2 text-sm text-emerald-700'
    }
    if (status.kind === 'error') {
      return 'rounded border border-rose-200 bg-rose-50 p-2 text-sm text-rose-700'
    }
    return ''
  }, [status.kind])

  const parseDraft = (): FlowDraft => {
    const parsedNodes = JSON.parse(nodesJson) as FlowDraft['nodes']
    const parsedConnections = JSON.parse(connectionsJson) as FlowDraft['connections']

    if (!Array.isArray(parsedNodes)) {
      throw new Error('Nodes JSON must be an array.')
    }
    if (!Array.isArray(parsedConnections)) {
      throw new Error('Connections JSON must be an array.')
    }

    return {
      name,
      description,
      nodes: parsedNodes,
      connections: parsedConnections,
    }
  }

  const setDraft = (draft: FlowDraft) => {
    const next = toEditorState(draft)
    setName(next.name)
    setDescription(next.description)
    setNodesJson(next.nodesJson)
    setConnectionsJson(next.connectionsJson)
  }

  const loadFlow = async () => {
    if (!flowIdInput.trim()) {
      setStatus({ kind: 'error', message: 'Enter a flow ID before loading.' })
      return
    }

    setIsBusy(true)
    setStatus({ kind: 'idle', message: '' })
    try {
      const response = await fetch(`${serverBase}/api/flows/${flowIdInput.trim()}`, {
        headers: buildHeaders(false),
      })
      if (!response.ok) {
        throw new Error(await readErrorMessage(response))
      }
      const flow = (await response.json()) as FlowDefinition
      const draft = toFlowDraft(flow)
      setFlowIdInput(flow.id)
      setDraft(draft)
      setCurrentVersion(flow.version)
      setStatus({ kind: 'success', message: `Loaded flow ${flow.id} (version ${flow.version}).` })
      onFlowSelected(flow.id, flow.version)
    } catch (error) {
      setStatus({ kind: 'error', message: error instanceof Error ? error.message : 'Failed to load flow.' })
    } finally {
      setIsBusy(false)
    }
  }

  const saveFlow = async (mode: 'create' | 'update') => {
    let draft: FlowDraft
    try {
      draft = parseDraft()
    } catch (error) {
      setStatus({ kind: 'error', message: error instanceof Error ? error.message : 'Invalid JSON.' })
      return
    }

    const validationErrors = validateFlowDraft(draft)
    if (validationErrors.length > 0) {
      setStatus({ kind: 'error', message: validationErrors[0] })
      return
    }

    if (mode === 'update' && !flowIdInput.trim()) {
      setStatus({ kind: 'error', message: 'Enter a flow ID before saving a new version.' })
      return
    }

    setIsBusy(true)
    setStatus({ kind: 'idle', message: '' })
    try {
      const url = mode === 'create'
        ? `${serverBase}/api/flows`
        : `${serverBase}/api/flows/${flowIdInput.trim()}`
      const method = mode === 'create' ? 'POST' : 'PUT'
      const response = await fetch(url, {
        method,
        headers: buildHeaders(true),
        body: JSON.stringify({
          ...buildFlowWritePayload(draft),
          lifecycleState,
          personaKeys: personaKeys
            .split(',')
            .map((value) => value.trim())
            .filter((value) => value.length > 0),
        }),
      })

      if (!response.ok) {
        throw new Error(await readErrorMessage(response))
      }

      const savedFlow = (await response.json()) as FlowDefinition
      const savedDraft = toFlowDraft(savedFlow)
      setFlowIdInput(savedFlow.id)
      setCurrentVersion(savedFlow.version)
      setDraft(savedDraft)
      setStatus({
        kind: 'success',
        message:
          mode === 'create'
            ? `Created flow ${savedFlow.id} (version ${savedFlow.version}).`
            : `Saved flow ${savedFlow.id} as version ${savedFlow.version}.`,
      })
      onFlowSelected(savedFlow.id, savedFlow.version)
    } catch (error) {
      setStatus({ kind: 'error', message: error instanceof Error ? error.message : 'Failed to save flow.' })
    } finally {
      setIsBusy(false)
    }
  }

  const deleteFlow = async () => {
    if (!flowIdInput.trim()) {
      setStatus({ kind: 'error', message: 'Enter a flow ID before deleting.' })
      return
    }

    setIsBusy(true)
    setStatus({ kind: 'idle', message: '' })
    try {
      const response = await fetch(`${serverBase}/api/flows/${flowIdInput.trim()}`, {
        method: 'DELETE',
        headers: buildHeaders(false),
      })

      if (!response.ok) {
        throw new Error(await readErrorMessage(response))
      }

      const reset = createDefaultFlowDraft()
      setFlowIdInput('')
      setCurrentVersion(null)
      setDraft(reset)
      setStatus({ kind: 'success', message: 'Flow deleted.' })
      onFlowSelected(null)
      setLifecycleState('Draft')
      setPersonaKeys('')
    } catch (error) {
      setStatus({ kind: 'error', message: error instanceof Error ? error.message : 'Failed to delete flow.' })
    } finally {
      setIsBusy(false)
    }
  }

  const reloadAndVerify = async () => {
    if (!flowIdInput.trim()) {
      setStatus({ kind: 'error', message: 'Enter a flow ID before reloading.' })
      return
    }

    let currentDraft: FlowDraft
    try {
      currentDraft = parseDraft()
    } catch (error) {
      setStatus({ kind: 'error', message: error instanceof Error ? error.message : 'Invalid JSON.' })
      return
    }

    setIsBusy(true)
    setStatus({ kind: 'idle', message: '' })
    try {
      const response = await fetch(`${serverBase}/api/flows/${flowIdInput.trim()}`, {
        headers: buildHeaders(false),
      })
      if (!response.ok) {
        throw new Error(await readErrorMessage(response))
      }
      const reloaded = (await response.json()) as FlowDefinition
      const reloadedDraft = toFlowDraft(reloaded)
      const graphMatches = areDraftGraphsEqual(currentDraft, reloadedDraft)
      setCurrentVersion(reloaded.version)
      setDraft(reloadedDraft)
      setStatus({
        kind: graphMatches ? 'success' : 'error',
        message: graphMatches
          ? 'Reload verification passed: API graph matches the saved graph.'
          : 'Reload verification failed: API graph differs from the local graph.',
      })
      onFlowSelected(reloaded.id, reloaded.version)
    } catch (error) {
      setStatus({ kind: 'error', message: error instanceof Error ? error.message : 'Failed to reload flow.' })
    } finally {
      setIsBusy(false)
    }
  }

  const resetDraft = () => {
    const draft = createDefaultFlowDraft()
    setFlowIdInput('')
    setCurrentVersion(null)
    setDraft(draft)
    setStatus({ kind: 'success', message: 'Started a new draft flow.' })
    onFlowSelected(null)
    setLifecycleState('Draft')
    setPersonaKeys('')
  }

  return (
    <section className="space-y-3 rounded-lg border border-slate-200 bg-white p-4">
      <h2 className="text-lg font-semibold text-slate-900">Flow Authoring</h2>
      <p className="text-sm text-slate-600">
        Create, edit, version, delete, and verify flows through the UI.
      </p>

      <div className="grid gap-3 md:grid-cols-2">
        <label className="space-y-1 text-sm text-slate-700">
          Flow ID (for load/update/delete)
          <input
            className="w-full rounded border border-slate-300 px-3 py-2 text-sm"
            value={flowIdInput}
            onChange={(event) => setFlowIdInput(event.target.value)}
            placeholder="00000000-0000-0000-0000-000000000000"
          />
        </label>
        <label className="space-y-1 text-sm text-slate-700">
          Flow name
          <input
            className="w-full rounded border border-slate-300 px-3 py-2 text-sm"
            value={name}
            onChange={(event) => setName(event.target.value)}
          />
        </label>
      </div>

      <div className="grid gap-3 md:grid-cols-2">
        <label className="space-y-1 text-sm text-slate-700">
          Lifecycle state
          <select
            className="w-full rounded border border-slate-300 px-3 py-2 text-sm"
            value={lifecycleState}
            onChange={(event) => setLifecycleState(event.target.value as 'Draft' | 'Published')}
          >
            <option value="Draft">Draft</option>
            <option value="Published">Published</option>
          </select>
        </label>
        <label className="space-y-1 text-sm text-slate-700">
          Persona keys (comma-separated)
          <input
            className="w-full rounded border border-slate-300 px-3 py-2 text-sm"
            value={personaKeys}
            onChange={(event) => setPersonaKeys(event.target.value)}
            placeholder="new-user, enterprise-admin"
          />
        </label>
      </div>

      <label className="space-y-1 text-sm text-slate-700">
        Description
        <input
          className="w-full rounded border border-slate-300 px-3 py-2 text-sm"
          value={description}
          onChange={(event) => setDescription(event.target.value)}
        />
      </label>

      <div className="grid gap-3 md:grid-cols-2">
        <label className="space-y-1 text-sm text-slate-700">
          Nodes JSON
          <textarea
            className="h-56 w-full rounded border border-slate-300 px-3 py-2 text-xs font-mono"
            value={nodesJson}
            onChange={(event) => setNodesJson(event.target.value)}
          />
        </label>
        <label className="space-y-1 text-sm text-slate-700">
          Connections JSON
          <textarea
            className="h-56 w-full rounded border border-slate-300 px-3 py-2 text-xs font-mono"
            value={connectionsJson}
            onChange={(event) => setConnectionsJson(event.target.value)}
          />
        </label>
      </div>

      {currentVersion !== null ? (
        <p className="text-sm text-slate-600">Current version: {currentVersion}</p>
      ) : null}

      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          disabled={isBusy}
          onClick={() => void loadFlow()}
          className="rounded border border-slate-300 bg-white px-3 py-2 text-sm text-slate-700 disabled:opacity-50"
        >
          Load flow
        </button>
        <button
          type="button"
          disabled={isBusy}
          onClick={() => void saveFlow('create')}
          className="rounded bg-slate-900 px-3 py-2 text-sm text-white disabled:opacity-50"
        >
          Create flow
        </button>
        <button
          type="button"
          disabled={isBusy}
          onClick={() => void saveFlow('update')}
          className="rounded bg-slate-900 px-3 py-2 text-sm text-white disabled:opacity-50"
        >
          Save new version
        </button>
        <button
          type="button"
          disabled={isBusy}
          onClick={() => void reloadAndVerify()}
          className="rounded border border-slate-300 bg-white px-3 py-2 text-sm text-slate-700 disabled:opacity-50"
        >
          Reload and verify
        </button>
        <button
          type="button"
          disabled={isBusy}
          onClick={() => void deleteFlow()}
          className="rounded border border-rose-300 bg-white px-3 py-2 text-sm text-rose-700 disabled:opacity-50"
        >
          Delete flow
        </button>
        <button
          type="button"
          disabled={isBusy}
          onClick={resetDraft}
          className="rounded border border-slate-300 bg-white px-3 py-2 text-sm text-slate-700 disabled:opacity-50"
        >
          New draft
        </button>
      </div>

      {status.message ? (
        <p className={statusClassName} role={status.kind === 'error' ? 'alert' : undefined} aria-live={status.kind === 'error' ? 'assertive' : 'polite'}>
          {status.message}
        </p>
      ) : null}
    </section>
  )
}
