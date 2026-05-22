import { describe, it, expect } from 'vitest'
import type { FlowDraftConnection } from './flowAuthoring'
import type { NodeType } from '../onboarding/types/flow'
import {
  NODE_TYPE_STYLES,
  getNodeStyle,
  connectionToEdgeId,
  draftConnectionsToEdges,
} from './VisualJourneyCanvas'

describe('NODE_TYPE_STYLES', () => {
  const types: NodeType[] = ['Form', 'DocumentUpload', 'Redirect', 'Information', 'Logic']

  it.each(types)('defines a style entry for %s', (type) => {
    expect(NODE_TYPE_STYLES[type]).toBeDefined()
    expect(NODE_TYPE_STYLES[type].background).toBeTruthy()
    expect(NODE_TYPE_STYLES[type].borderColor).toBeTruthy()
  })
})

describe('getNodeStyle', () => {
  const types: NodeType[] = ['Form', 'DocumentUpload', 'Redirect', 'Information', 'Logic']

  it.each(types)('returns correct background color for %s', (type) => {
    const style = getNodeStyle({ type, isStartNode: false })
    expect(style.background).toBe(NODE_TYPE_STYLES[type].background)
  })

  it.each(types)('returns correct border color for %s', (type) => {
    const style = getNodeStyle({ type, isStartNode: false })
    expect(style.borderColor).toBe(NODE_TYPE_STYLES[type].borderColor)
  })

  it('uses bold font weight for start nodes', () => {
    const style = getNodeStyle({ type: 'Form', isStartNode: true })
    expect(style.fontWeight).toBe(700)
  })

  it('uses normal font weight for non-start nodes', () => {
    const style = getNodeStyle({ type: 'Form', isStartNode: false })
    expect(style.fontWeight).toBe(400)
  })

  it('applies outline to start nodes for distinctive treatment', () => {
    const startStyle = getNodeStyle({ type: 'Form', isStartNode: true })
    const regularStyle = getNodeStyle({ type: 'Form', isStartNode: false })
    expect(startStyle.outline).toBeTruthy()
    expect(regularStyle.outline).toBeFalsy()
  })
})

describe('connectionToEdgeId', () => {
  it('generates a stable ID from source, target, and priority', () => {
    const conn: FlowDraftConnection = {
      sourceNodeId: 'node-a',
      targetNodeId: 'node-b',
      priority: 0,
    }
    expect(connectionToEdgeId(conn)).toBe('node-a__node-b__0')
  })

  it('generates different IDs for different priorities', () => {
    const base: FlowDraftConnection = { sourceNodeId: 'a', targetNodeId: 'b', priority: 0 }
    const other: FlowDraftConnection = { ...base, priority: 1 }
    expect(connectionToEdgeId(base)).not.toBe(connectionToEdgeId(other))
  })

  it('generates different IDs for different sources', () => {
    const a: FlowDraftConnection = { sourceNodeId: 'x', targetNodeId: 'z', priority: 0 }
    const b: FlowDraftConnection = { sourceNodeId: 'y', targetNodeId: 'z', priority: 0 }
    expect(connectionToEdgeId(a)).not.toBe(connectionToEdgeId(b))
  })
})

describe('draftConnectionsToEdges', () => {
  it('maps each connection to a ReactFlow edge', () => {
    const connections: FlowDraftConnection[] = [
      { sourceNodeId: 'a', targetNodeId: 'b', priority: 0 },
      { sourceNodeId: 'b', targetNodeId: 'c', priority: 0 },
    ]
    const edges = draftConnectionsToEdges(connections)
    expect(edges).toHaveLength(2)
  })

  it('assigns stable edge IDs derived from connection fields', () => {
    const conn: FlowDraftConnection = { sourceNodeId: 'src', targetNodeId: 'tgt', priority: 2 }
    const [edge] = draftConnectionsToEdges([conn])
    expect(edge.id).toBe('src__tgt__2')
    expect(edge.source).toBe('src')
    expect(edge.target).toBe('tgt')
  })

  it('builds a label from all three condition parts', () => {
    const conn: FlowDraftConnection = {
      sourceNodeId: 'a',
      targetNodeId: 'b',
      conditionField: 'country',
      conditionOperator: 'Equals',
      conditionValue: 'US',
      priority: 0,
    }
    const [edge] = draftConnectionsToEdges([conn])
    expect(edge.label).toBe('country Equals US')
  })

  it('omits label when no condition fields are present', () => {
    const conn: FlowDraftConnection = { sourceNodeId: 'a', targetNodeId: 'b', priority: 0 }
    const [edge] = draftConnectionsToEdges([conn])
    expect(edge.label).toBeUndefined()
  })

  it('omits null condition parts from label', () => {
    const conn: FlowDraftConnection = {
      sourceNodeId: 'a',
      targetNodeId: 'b',
      conditionField: 'status',
      conditionOperator: null,
      conditionValue: null,
      priority: 0,
    }
    const [edge] = draftConnectionsToEdges([conn])
    expect(edge.label).toBe('status')
  })

  it('returns an empty array for no connections', () => {
    expect(draftConnectionsToEdges([])).toEqual([])
  })
})
