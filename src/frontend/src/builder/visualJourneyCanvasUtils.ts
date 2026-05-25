import type { CSSProperties } from 'react'
import type { Edge } from 'reactflow'
import type { FlowDraftNode, FlowDraftConnection } from './flowAuthoring'
import type { NodeType } from '../onboarding/types/flow'

export const NODE_TYPE_STYLES: Record<
  NodeType,
  { background: string; borderColor: string; color: string }
> = {
  Form: { background: '#dbeafe', borderColor: '#3b82f6', color: '#1e3a8a' },
  DocumentUpload: { background: '#ede9fe', borderColor: '#8b5cf6', color: '#4c1d95' },
  Redirect: { background: '#fef3c7', borderColor: '#f59e0b', color: '#78350f' },
  Information: { background: '#d1fae5', borderColor: '#10b981', color: '#064e3b' },
  Logic: { background: '#ffedd5', borderColor: '#f97316', color: '#7c2d12' },
}

export function getNodeStyle(
  node: Pick<FlowDraftNode, 'type' | 'isStartNode'>,
): CSSProperties {
  const colors = NODE_TYPE_STYLES[node.type]
  return {
    background: colors.background,
    borderColor: colors.borderColor,
    color: colors.color,
    fontWeight: node.isStartNode ? 700 : 400,
    border: `2px solid ${colors.borderColor}`,
    borderRadius: 8,
    padding: '6px 10px',
    fontSize: 12,
    outline: node.isStartNode ? `3px solid ${colors.borderColor}` : undefined,
    outlineOffset: node.isStartNode ? 3 : undefined,
  }
}

export function connectionToEdgeId(conn: FlowDraftConnection): string {
  return `${conn.sourceNodeId}__${conn.targetNodeId}__${conn.priority}`
}

export function draftConnectionsToEdges(connections: FlowDraftConnection[]): Edge[] {
  return connections.map((conn) => {
    const parts = [conn.conditionField, conn.conditionOperator, conn.conditionValue].filter(Boolean)
    return {
      id: connectionToEdgeId(conn),
      source: conn.sourceNodeId,
      target: conn.targetNodeId,
      label: parts.length > 0 ? (parts.join(' ') as string) : undefined,
    }
  })
}
