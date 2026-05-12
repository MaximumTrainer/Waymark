export type NodeType = 'Form' | 'DocumentUpload' | 'Redirect' | 'Information' | 'Logic'

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
}

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
  nodes: FlowNodeDetail[]
  connections: FlowConnection[]
}
