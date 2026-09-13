import type { SessionStartResponse, SessionStepResponse, StartSessionRequest, SubmitStepRequest } from '../types/flow'
import { applicantAuthHeaders, setApplicantToken } from './applicant-session'

export interface ComplianceViolation {
  field: string
  message: string
  ruleId?: string
}

export class ComplianceError extends Error {
  readonly violations: ComplianceViolation[]

  constructor(violations: ComplianceViolation[]) {
    super('Compliance validation failed')
    this.name = 'ComplianceError'
    this.violations = violations
  }
}

const WORKFLOW_API_BASE_PATH = '/api/workflow'

/**
 * Requests carry the per-session applicant token issued at session start. No API key is sent from
 * the browser: the key maps to a full Operator principal and inlining it in the bundle would hand
 * every visitor operator access.
 */
function buildHeaders(): Record<string, string> {
  return { 'Content-Type': 'application/json', ...applicantAuthHeaders() }
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
): Promise<SessionStepResponse> {
  const workflowBase = resolveWorkflowApiBase(baseUrl)
  // Anonymous by design: this is the entry point of a public journey, and the response is what
  // hands the browser its credential for everything that follows.
  const res = await fetch(`${workflowBase}/sessions/start`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  if (!res.ok) throw new Error(`startSession failed with status ${res.status}`)

  const session = await res.json() as SessionStartResponse
  if (session.applicantToken) setApplicantToken(session.applicantToken)

  return session
}

export async function submitStep(
  baseUrl: string,
  sessionId: string,
  nodeId: string,
  payload: SubmitStepRequest,
): Promise<SessionStepResponse> {
  const workflowBase = resolveWorkflowApiBase(baseUrl)
  const res = await fetch(
    `${workflowBase}/sessions/${sessionId}/steps/${nodeId}/submit`,
    {
      method: 'POST',
      headers: buildHeaders(),
      body: JSON.stringify(payload),
    },
  )
  if (res.status === 422) {
    const problem = await res.json() as { violations?: ComplianceViolation[] }
    throw new ComplianceError(problem.violations ?? [])
  }
  if (!res.ok) throw new Error(`submitStep failed with status ${res.status}`)
  return res.json() as Promise<SessionStepResponse>
}

export async function getNextStep(
  baseUrl: string,
  sessionId: string,
): Promise<SessionStepResponse> {
  const workflowBase = resolveWorkflowApiBase(baseUrl)
  const res = await fetch(`${workflowBase}/sessions/${sessionId}/next`, {
    method: 'GET',
    headers: buildHeaders(),
  })
  if (!res.ok) throw new Error(`getNextStep failed with status ${res.status}`)
  return res.json() as Promise<SessionStepResponse>
}
