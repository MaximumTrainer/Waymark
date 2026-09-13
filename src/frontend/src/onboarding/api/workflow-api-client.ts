import type { SessionStartResponse, SessionStepResponse, StartSessionRequest, SubmitStepRequest } from '../types/flow'
import { applicantAuthHeaders, setApplicantToken } from './applicant-session'

export interface ComplianceViolation {
  field: string
  message: string
  ruleId?: string
}

/**
 * The applicant credential is no longer accepted — it expired, or its session ended.
 *
 * Distinct from a generic failure on purpose. An expired credential is not a fault the applicant
 * can retry past: the only way forward is to start again, and the page has to say so rather than
 * showing a server-error banner over a journey that will never resume.
 */
export class ExpiredSessionError extends Error {
  constructor() {
    super('Your session has expired. Please start again.')
    this.name = 'ExpiredSessionError'
  }
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
  captureRefreshedToken(session)

  return session
}

/**
 * Stores a credential the API handed back, if it handed one back.
 *
 * Session start always carries one; step submission carries a renewal, which is what keeps a long
 * journey from outliving its own token. An operator-submitted step carries none, and the stored
 * credential is left alone.
 */
function captureRefreshedToken(response: SessionStepResponse): void {
  if (response.applicantToken) setApplicantToken(response.applicantToken)
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
  if (isCredentialRejection(res.status)) throw new ExpiredSessionError()
  if (!res.ok) throw new Error(`submitStep failed with status ${res.status}`)

  const next = await res.json() as SessionStepResponse
  captureRefreshedToken(next)

  return next
}

/**
 * Whether a status means the credential is the problem.
 *
 * 401 is an expired or unreadable token. 403 on a session the browser was completing means the
 * session reached a terminal status, after which its own token is refused for writes — from the
 * applicant's side the two are the same dead end.
 */
function isCredentialRejection(status: number): boolean {
  return status === 401 || status === 403
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
  if (res.status === 401) throw new ExpiredSessionError()
  if (!res.ok) throw new Error(`getNextStep failed with status ${res.status}`)
  return res.json() as Promise<SessionStepResponse>
}
