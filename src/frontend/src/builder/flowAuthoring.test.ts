import { describe, expect, it } from 'vitest'
import {
  areDraftGraphsEqual,
  buildFlowWritePayload,
  createDefaultFlowDraft,
  validateFlowDraft,
} from './flowAuthoring'

describe('flowAuthoring validation', () => {
  it('rejects invalid connections before save', () => {
    const draft = createDefaultFlowDraft()
    draft.connections = [
      {
        sourceNodeId: draft.nodes[0].id,
        targetNodeId: '00000000-0000-0000-0000-000000000999',
        priority: 0,
      },
    ]

    const errors = validateFlowDraft(draft)
    expect(errors).toContain('Connection #1: sourceNodeId and targetNodeId must reference nodes in this flow.')
  })

  it('accepts a valid draft and trims payload values', () => {
    const draft = createDefaultFlowDraft()
    draft.name = '  My flow  '
    draft.nodes[0].key = '  start  '
    draft.nodes[0].title = '  Start  '

    const errors = validateFlowDraft(draft)
    expect(errors).toEqual([])

    const payload = buildFlowWritePayload(draft)
    expect(payload.name).toBe('My flow')
    expect(payload.nodes[0].key).toBe('start')
    expect(payload.nodes[0].title).toBe('Start')
  })

  it('treats re-ordered nodes and connections as identical graph', () => {
    const left = {
      name: 'Flow',
      description: '',
      nodes: [
        { id: 'a', key: 'start', type: 'Form' as const, title: 'Start', jsonContent: '{}', isStartNode: true },
        { id: 'b', key: 'next', type: 'Form' as const, title: 'Next', jsonContent: '{}', isStartNode: false },
      ],
      connections: [{ sourceNodeId: 'a', targetNodeId: 'b', priority: 0 }],
    }
    const right = {
      name: 'Flow',
      description: '',
      nodes: [...left.nodes].reverse(),
      connections: [...left.connections],
    }

    expect(areDraftGraphsEqual(left, right)).toBe(true)
  })
})
