import { describe, expect, it } from 'vitest'
import {
  areDraftGraphsEqual,
  buildFlowWritePayload,
  createDefaultFlowDraft,
  toFlowDraft,
  validateFlowDraft,
} from './flowAuthoring'

describe('flowAuthoring validation', () => {
  it('rejects missing flow name', () => {
    const draft = createDefaultFlowDraft()
    draft.name = '   '

    const errors = validateFlowDraft(draft)
    expect(errors).toContain('Flow name is required.')
  })

  it('rejects missing nodes', () => {
    const draft = createDefaultFlowDraft()
    draft.nodes = []

    const errors = validateFlowDraft(draft)
    expect(errors).toContain('At least one node is required.')
  })

  it('rejects duplicate node ids', () => {
    const draft = createDefaultFlowDraft()
    const duplicateId = '11111111-1111-1111-8111-111111111111'
    draft.nodes = [
      { ...draft.nodes[0], id: duplicateId, isStartNode: true },
      { ...draft.nodes[0], id: duplicateId, key: 'next', title: 'Next', isStartNode: false },
    ]

    const errors = validateFlowDraft(draft)
    expect(errors).toContain('Node #2: id must be unique.')
  })

  it('rejects node ids that are not GUIDs', () => {
    const draft = createDefaultFlowDraft()
    draft.nodes[0].id = 'not-a-guid'

    const errors = validateFlowDraft(draft)
    expect(errors).toContain('Node #1: id must be a valid GUID.')
  })

  it('rejects invalid start-node count', () => {
    const draft = createDefaultFlowDraft()
    draft.nodes = [
      { ...draft.nodes[0], id: '11111111-1111-1111-8111-111111111111', isStartNode: true },
      { ...draft.nodes[0], id: '22222222-2222-2222-8222-222222222222', key: 'next', title: 'Next', isStartNode: true },
    ]

    const errors = validateFlowDraft(draft)
    expect(errors).toContain('Exactly one start node is required.')
  })

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

  it('rejects non-integer connection priority', () => {
    const draft = createDefaultFlowDraft()
    draft.connections = [
      {
        sourceNodeId: draft.nodes[0].id,
        targetNodeId: draft.nodes[0].id,
        priority: 0.5,
      },
    ]

    const errors = validateFlowDraft(draft)
    expect(errors).toContain('Connection #1: priority must be a non-negative integer.')
  })

  it('accepts a valid draft and trims payload values', () => {
    const draft = createDefaultFlowDraft()
    draft.name = '  My flow  '
    draft.description = '   '
    draft.nodes[0].key = '  start  '
    draft.nodes[0].title = '  Start  '
    draft.nodes[0].complianceRuleJson = '  {"rule":"x"}  '
    draft.connections = [
      {
        sourceNodeId: draft.nodes[0].id,
        targetNodeId: draft.nodes[0].id,
        conditionField: '  country ',
        conditionOperator: ' Equals ',
        conditionValue: ' US ',
        priority: 0,
      },
    ]

    const errors = validateFlowDraft(draft)
    expect(errors).toEqual([])

    const payload = buildFlowWritePayload(draft)
    expect(payload.name).toBe('My flow')
    expect(payload.description).toBeNull()
    expect(payload.nodes[0].key).toBe('start')
    expect(payload.nodes[0].title).toBe('Start')
    expect(payload.nodes[0].complianceRuleJson).toBe('{"rule":"x"}')
    expect(payload.connections[0].conditionField).toBe('country')
    expect(payload.connections[0].conditionOperator).toBe('Equals')
    expect(payload.connections[0].conditionValue).toBe('US')
  })

  it('sets empty node jsonContent to default object string in payload', () => {
    const draft = createDefaultFlowDraft()
    draft.nodes[0].jsonContent = ''

    const payload = buildFlowWritePayload(draft)
    expect(payload.nodes[0].jsonContent).toBe('{}')
  })

  it('sets whitespace-only node jsonContent to default object string in payload', () => {
    const draft = createDefaultFlowDraft()
    draft.nodes[0].jsonContent = '   '

    const payload = buildFlowWritePayload(draft)
    expect(payload.nodes[0].jsonContent).toBe('{}')
  })

  it('allows empty jsonContent during validation because payload normalizes it', () => {
    const draft = createDefaultFlowDraft()
    draft.nodes[0].jsonContent = '   '

    const errors = validateFlowDraft(draft)
    expect(errors).toEqual([])
  })

  it('maps flow definition data into draft shape', () => {
    const draft = toFlowDraft({
      id: 'flow-id',
      name: 'Flow',
      description: null,
      version: 2,
      nodes: [
        {
          id: 'node-a',
          key: 'start',
          type: 'Form',
          title: 'Start',
          jsonContent: '{}',
          complianceRuleJson: '{"rule":"match"}',
          isStartNode: true,
        },
      ],
      connections: [
        {
          id: 'edge-1',
          sourceNodeId: 'node-a',
          targetNodeId: 'node-a',
          conditionField: null,
          conditionOperator: null,
          conditionValue: null,
          priority: 0,
        },
      ],
    })

    expect(draft.description).toBe('')
    expect(draft.nodes[0].complianceRuleJson).toBe('{"rule":"match"}')
    expect(draft.connections[0].conditionField).toBeNull()
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

  it('detects compliance rule differences as graph changes', () => {
    const left = createDefaultFlowDraft()
    const right = createDefaultFlowDraft()
    left.nodes[0].complianceRuleJson = '{"rule":"a"}'
    right.nodes[0].complianceRuleJson = '{"rule":"b"}'

    expect(areDraftGraphsEqual(left, right)).toBe(false)
  })

  it('treats null and empty optional graph fields as equivalent', () => {
    const left = {
      name: 'Flow',
      description: null,
      nodes: [
        { id: 'a', key: 'start', type: 'Form' as const, title: 'Start', jsonContent: '{}', isStartNode: true },
      ],
      connections: [{ sourceNodeId: 'a', targetNodeId: 'a', conditionField: null, conditionOperator: null, conditionValue: null, priority: 0 }],
    }
    const right = {
      ...left,
      description: '',
      connections: [{ sourceNodeId: 'a', targetNodeId: 'a', conditionField: '', conditionOperator: '', conditionValue: '', priority: 0 }],
    }

    expect(areDraftGraphsEqual(left, right)).toBe(true)
  })
})
