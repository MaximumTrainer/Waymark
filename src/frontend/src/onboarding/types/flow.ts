export type NodeType =
  | 'Form'
  | 'DocumentUpload'
  | 'Redirect'
  | 'Information'
  | 'Logic'

export interface FlowNode {
  id: string
  key: string
  type: NodeType
  title: string
  jsonContent: string
}

export interface SessionStepResponse {
  sessionId: string
  isCompleted: boolean
  currentNode: FlowNode | null
  /**
   * A replacement credential, returned on step submission so a journey still being worked on never
   * outlives its own token. Absent when an operator submits: their credential already covers the
   * session.
   */
  applicantToken?: string | null
  applicantTokenExpiresAt?: string | null
}

/** Session start also returns the credential used for the rest of the journey. */
export type SessionStartResponse = SessionStepResponse

export interface StartSessionRequest {
  flowId: string
  customerProfileId?: string
}

export interface SubmitStepRequest {
  payload: Record<string, unknown>
}

// ── Flow definition (from GET /api/flows/{id}) ────────────────────────────────

export interface FlowNodeDetail {
  id: string
  key: string
  type: NodeType
  title: string
  jsonContent: string
  complianceRuleJson?: string | null
  isStartNode: boolean
}

export interface FlowConnection {
  id: string
  sourceNodeId: string
  targetNodeId: string
  conditionField?: string | null
  conditionOperator?: string | null
  conditionValue?: string | null
  priority: number
}

export interface FlowDefinition {
  id: string
  name: string
  description?: string | null
  version: number
  lifecycleState?: 'Draft' | 'Published'
  personaKeys?: string[]
  nodes: FlowNodeDetail[]
  connections: FlowConnection[]
}
