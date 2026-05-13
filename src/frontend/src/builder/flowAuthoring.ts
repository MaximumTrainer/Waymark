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
// Format-only GUID check to match backend Guid parsing rules (not RFC version/variant enforcement).
const GUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

function isGuid(value: string): boolean {
  return GUID_REGEX.test(value)
}

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
      complianceRuleJson: node.complianceRuleJson ?? null,
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
      jsonContent: node.jsonContent.trim() || '{}',
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
    const nodeId = node.id.trim()
    if (!nodeId) {
      errors.push(`${nodeLabel}: id is required.`)
    } else if (!isGuid(nodeId)) {
      errors.push(`${nodeLabel}: id must be a valid GUID.`)
    } else if (nodeIds.has(nodeId)) {
      errors.push(`${nodeLabel}: id must be unique.`)
    } else {
      nodeIds.add(nodeId)
    }

    if (!node.key.trim()) errors.push(`${nodeLabel}: key is required.`)
    if (!node.title.trim()) errors.push(`${nodeLabel}: title is required.`)
    if (!node.type) errors.push(`${nodeLabel}: type is required.`)
    if (node.isStartNode) startNodeCount += 1
  })

  if (startNodeCount !== 1) {
    errors.push('Exactly one start node is required.')
  }

  draft.connections.forEach((connection, index) => {
    const connectionLabel = `Connection #${index + 1}`
    const sourceNodeId = connection.sourceNodeId.trim()
    const targetNodeId = connection.targetNodeId.trim()

    if (!sourceNodeId) {
      errors.push(`${connectionLabel}: sourceNodeId is required.`)
      return
    }

    if (!isGuid(sourceNodeId)) {
      errors.push(`${connectionLabel}: sourceNodeId must be a valid GUID.`)
      return
    }

    if (!targetNodeId) {
      errors.push(`${connectionLabel}: targetNodeId is required.`)
      return
    }

    if (!isGuid(targetNodeId)) {
      errors.push(`${connectionLabel}: targetNodeId must be a valid GUID.`)
      return
    }

    if (!Number.isInteger(connection.priority) || connection.priority < 0) {
      errors.push(`${connectionLabel}: priority must be a non-negative integer.`)
      return
    }

    if (!nodeIds.has(sourceNodeId) || !nodeIds.has(targetNodeId)) {
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
        jsonContent: node.jsonContent.trim() || '{}',
        complianceRuleJson: node.complianceRuleJson?.trim() ?? '',
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
