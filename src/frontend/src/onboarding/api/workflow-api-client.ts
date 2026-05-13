import type { SessionStepResponse, StartSessionRequest, SubmitStepRequest } from '../types/flow'

const DEFAULT_API_KEY = import.meta.env.VITE_API_KEY ?? ''
const WORKFLOW_API_BASE_PATH = '/api/workflow'

function buildHeaders(apiKey?: string): Record<string, string> {
  const key = apiKey ?? DEFAULT_API_KEY
  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  if (key) headers['X-Api-Key'] = key
  return headers
}

export function resolveWorkflowApiBase(baseUrl: string): string {
  const normalizedBase = (baseUrl ?? '').trim().replace(/\/+$/, '')
  if (!normalizedBase) return WORKFLOW_API_BASE_PATH
  return normalizedBase.endsWith(WORKFLOW_API_BASE_PATH)
    ? normalizedBase
    : `${normalizedBase}${WORKFLOW_API_BASE_PATH}`
}

export async function startSession(
  baseUrl: string,
  payload: StartSessionRequest,
  apiKey?: string,
): Promise<SessionStepResponse> {
  const workflowBase = resolveWorkflowApiBase(baseUrl)
  const res = await fetch(`${workflowBase}/sessions/start`, {
    method: 'POST',
    headers: buildHeaders(apiKey),
    body: JSON.stringify(payload),
  })
  if (!res.ok) throw new Error(`startSession failed with status ${res.status}`)
  return res.json() as Promise<SessionStepResponse>
}

export async function submitStep(
  baseUrl: string,
  sessionId: string,
  nodeId: string,
  payload: SubmitStepRequest,
  apiKey?: string,
): Promise<SessionStepResponse> {
  const workflowBase = resolveWorkflowApiBase(baseUrl)
  const res = await fetch(
    `${workflowBase}/sessions/${sessionId}/steps/${nodeId}/submit`,
    {
      method: 'POST',
      headers: buildHeaders(apiKey),
      body: JSON.stringify(payload),
    },
  )
  if (!res.ok) throw new Error(`submitStep failed with status ${res.status}`)
  return res.json() as Promise<SessionStepResponse>
}

export async function getNextStep(
  baseUrl: string,
  sessionId: string,
  apiKey?: string,
): Promise<SessionStepResponse> {
  const workflowBase = resolveWorkflowApiBase(baseUrl)
  const res = await fetch(`${workflowBase}/sessions/${sessionId}/next`, {
    method: 'GET',
    headers: buildHeaders(apiKey),
  })
  if (!res.ok) throw new Error(`getNextStep failed with status ${res.status}`)
  return res.json() as Promise<SessionStepResponse>
}
