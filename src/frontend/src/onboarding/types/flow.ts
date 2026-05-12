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
