import type { FlowDefinition } from '../onboarding/types/flow'
import type { FlowDraft } from './flowAuthoring'
import { VisualJourneyBuilder } from './VisualJourneyBuilder'

const serverBase = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')
const apiKey = import.meta.env.VITE_API_KEY

function buildHeaders(includeJson: boolean): Record<string, string> {
  const headers: Record<string, string> = {}
  if (includeJson) headers['Content-Type'] = 'application/json'
  if (apiKey) headers['X-Api-Key'] = apiKey
  return headers
}

async function readErrorMessage(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as {
      title?: string
      errors?: Record<string, string[]>
    }
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

async function loadFlow(flowId: string): Promise<FlowDefinition> {
  const response = await fetch(`${serverBase}/api/flows/${flowId}`, {
    headers: buildHeaders(false),
  })
  if (!response.ok) throw new Error(await readErrorMessage(response))
  return (await response.json()) as FlowDefinition
}

async function saveFlow(flowId: string, draft: FlowDraft): Promise<FlowDefinition> {
  const response = await fetch(`${serverBase}/api/flows/${flowId}`, {
    method: 'PUT',
    headers: buildHeaders(true),
    body: JSON.stringify(draft),
  })
  if (!response.ok) throw new Error(await readErrorMessage(response))
  return (await response.json()) as FlowDefinition
}

async function createFlow(draft: FlowDraft): Promise<FlowDefinition> {
  const response = await fetch(`${serverBase}/api/flows`, {
    method: 'POST',
    headers: buildHeaders(true),
    body: JSON.stringify(draft),
  })
  if (!response.ok) throw new Error(await readErrorMessage(response))
  return (await response.json()) as FlowDefinition
}

export function AdminJourneyBuilderPage() {
  return (
    <main className="mx-auto max-w-screen-xl space-y-6 p-6">
      <header className="space-y-1">
        <h1 className="text-2xl font-bold text-slate-900">Visual Journey Builder</h1>
        <p className="text-sm text-slate-600">
          Design and publish onboarding flows visually. Drag nodes to reposition, connect nodes by
          dragging between handles, and select any node or edge to edit its properties.
        </p>
      </header>
      <VisualJourneyBuilder onLoad={loadFlow} onSave={saveFlow} onCreateNew={createFlow} />
    </main>
  )
}
