import type { FlowDefinition, NodeType } from '../onboarding/types/flow'

export interface FlowDraftNode {
  id: string
  key: string
  type: NodeType
  title: string
  jsonContent: string
  isStartNode: boolean
  complianceRuleJson?: string | null
}

export interface FlowDraftConnection {
  sourceNodeId: string
  targetNodeId: string
  conditionField?: string | null
  conditionOperator?: string | null
  conditionValue?: string | null
  priority: number
}

export interface FlowDraft {
  name: string
  description?: string | null
  nodes: FlowDraftNode[]
  connections: FlowDraftConnection[]
}

const NEW_FLOW_DEFAULT_NODE_ID = '11111111-1111-1111-1111-111111111111'

export function createDefaultFlowDraft(): FlowDraft {
  return {
    name: 'New Flow',
    description: '',
    nodes: [
      {
        id: NEW_FLOW_DEFAULT_NODE_ID,
        key: 'start',
        type: 'Form',
        title: 'Start',
        jsonContent: '{}',
        isStartNode: true,
      },
    ],
    connections: [],
  }
}

export function toFlowDraft(flow: FlowDefinition): FlowDraft {
  return {
    name: flow.name,
    description: flow.description ?? '',
    nodes: flow.nodes.map((node) => ({
      id: node.id,
      key: node.key,
      type: node.type,
      title: node.title,
      jsonContent: node.jsonContent,
      isStartNode: node.isStartNode,
    })),
    connections: flow.connections.map((connection) => ({
      sourceNodeId: connection.sourceNodeId,
      targetNodeId: connection.targetNodeId,
      conditionField: connection.conditionField ?? null,
      conditionOperator: connection.conditionOperator ?? null,
      conditionValue: connection.conditionValue ?? null,
      priority: connection.priority,
    })),
  }
}

export function buildFlowWritePayload(draft: FlowDraft): FlowDraft {
  return {
    name: draft.name.trim(),
    description: draft.description?.trim() || null,
    nodes: draft.nodes.map((node) => ({
      id: node.id,
      key: node.key.trim(),
      type: node.type,
      title: node.title.trim(),
      jsonContent: node.jsonContent || '{}',
      isStartNode: node.isStartNode,
      complianceRuleJson: node.complianceRuleJson?.trim() || null,
    })),
    connections: draft.connections.map((connection) => ({
      sourceNodeId: connection.sourceNodeId,
      targetNodeId: connection.targetNodeId,
      conditionField: connection.conditionField?.trim() || null,
      conditionOperator: connection.conditionOperator?.trim() || null,
      conditionValue: connection.conditionValue?.trim() || null,
      priority: connection.priority,
    })),
  }
}

export function validateFlowDraft(draft: FlowDraft): string[] {
  const errors: string[] = []
  if (!draft.name.trim()) {
    errors.push('Flow name is required.')
  }

  if (draft.nodes.length === 0) {
    errors.push('At least one node is required.')
    return errors
  }

  const nodeIds = new Set<string>()
  let startNodeCount = 0
  draft.nodes.forEach((node, index) => {
    const nodeLabel = `Node #${index + 1}`
    if (!node.id.trim()) {
      errors.push(`${nodeLabel}: id is required.`)
    } else if (nodeIds.has(node.id)) {
      errors.push(`${nodeLabel}: id must be unique.`)
    } else {
      nodeIds.add(node.id)
    }

    if (!node.key.trim()) errors.push(`${nodeLabel}: key is required.`)
    if (!node.title.trim()) errors.push(`${nodeLabel}: title is required.`)
    if (!node.type) errors.push(`${nodeLabel}: type is required.`)
    if (!node.jsonContent.trim()) errors.push(`${nodeLabel}: jsonContent is required.`)
    if (node.isStartNode) startNodeCount += 1
  })

  if (startNodeCount !== 1) {
    errors.push('Exactly one start node is required.')
  }

  draft.connections.forEach((connection, index) => {
    const connectionLabel = `Connection #${index + 1}`
    if (!connection.sourceNodeId.trim()) {
      errors.push(`${connectionLabel}: sourceNodeId is required.`)
      return
    }

    if (!connection.targetNodeId.trim()) {
      errors.push(`${connectionLabel}: targetNodeId is required.`)
      return
    }

    if (!nodeIds.has(connection.sourceNodeId) || !nodeIds.has(connection.targetNodeId)) {
      errors.push(`${connectionLabel}: sourceNodeId and targetNodeId must reference nodes in this flow.`)
    }
  })

  return errors
}

function normalizeDraftGraph(draft: FlowDraft) {
  return {
    name: draft.name.trim(),
    description: draft.description?.trim() ?? '',
    nodes: [...draft.nodes]
      .map((node) => ({
        id: node.id,
        key: node.key.trim(),
        type: node.type,
        title: node.title.trim(),
        jsonContent: node.jsonContent.trim(),
        isStartNode: node.isStartNode,
      }))
      .sort((a, b) => a.id.localeCompare(b.id)),
    connections: [...draft.connections]
      .map((connection) => ({
        sourceNodeId: connection.sourceNodeId,
        targetNodeId: connection.targetNodeId,
        conditionField: connection.conditionField?.trim() ?? '',
        conditionOperator: connection.conditionOperator?.trim() ?? '',
        conditionValue: connection.conditionValue?.trim() ?? '',
        priority: connection.priority,
      }))
      .sort((a, b) =>
        `${a.sourceNodeId}-${a.targetNodeId}-${a.priority}`.localeCompare(
          `${b.sourceNodeId}-${b.targetNodeId}-${b.priority}`,
        ),
      ),
  }
}

export function areDraftGraphsEqual(left: FlowDraft, right: FlowDraft): boolean {
  return JSON.stringify(normalizeDraftGraph(left)) === JSON.stringify(normalizeDraftGraph(right))
}
