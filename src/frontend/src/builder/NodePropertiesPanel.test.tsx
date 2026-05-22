import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { NodePropertiesPanel } from './NodePropertiesPanel'
import { createDefaultFlowDraft } from './flowAuthoring'

describe('NodePropertiesPanel', () => {
  it('shows placeholder when nothing is selected', () => {
    render(
      <NodePropertiesPanel
        draft={createDefaultFlowDraft()}
        onChange={vi.fn()}
        selectedNodeId={null}
        selectedEdgeId={null}
      />,
    )
    expect(screen.getByText(/select a node or edge/i)).toBeInTheDocument()
  })

  it('displays node properties when a node is selected', () => {
    const draft = createDefaultFlowDraft()
    const node = draft.nodes[0]
    render(
      <NodePropertiesPanel
        draft={draft}
        onChange={vi.fn()}
        selectedNodeId={node.id}
        selectedEdgeId={null}
      />,
    )
    expect(screen.getByDisplayValue(node.title)).toBeInTheDocument()
    expect(screen.getByDisplayValue(node.key)).toBeInTheDocument()
  })

  it('calls onChange when title is updated', () => {
    const draft = createDefaultFlowDraft()
    const onChange = vi.fn()
    render(
      <NodePropertiesPanel
        draft={draft}
        onChange={onChange}
        selectedNodeId={draft.nodes[0].id}
        selectedEdgeId={null}
      />,
    )
    fireEvent.change(screen.getByDisplayValue(draft.nodes[0].title), {
      target: { value: 'Updated Title' },
    })
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({
        nodes: expect.arrayContaining([expect.objectContaining({ title: 'Updated Title' })]),
      }),
    )
  })

  it('calls onChange when key is updated', () => {
    const draft = createDefaultFlowDraft()
    const onChange = vi.fn()
    render(
      <NodePropertiesPanel
        draft={draft}
        onChange={onChange}
        selectedNodeId={draft.nodes[0].id}
        selectedEdgeId={null}
      />,
    )
    fireEvent.change(screen.getByDisplayValue(draft.nodes[0].key), {
      target: { value: 'new-key' },
    })
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({
        nodes: expect.arrayContaining([expect.objectContaining({ key: 'new-key' })]),
      }),
    )
  })

  it('enforces single start node when isStartNode toggled on', () => {
    const defaultDraft = createDefaultFlowDraft()
    const draft = {
      ...defaultDraft,
      nodes: [
        { ...defaultDraft.nodes[0], id: 'node-a', isStartNode: true },
        {
          id: 'node-b',
          key: 'second',
          type: 'Form' as const,
          title: 'Second',
          jsonContent: '{}',
          isStartNode: false,
        },
      ],
    }
    const onChange = vi.fn()
    render(
      <NodePropertiesPanel
        draft={draft}
        onChange={onChange}
        selectedNodeId="node-b"
        selectedEdgeId={null}
      />,
    )
    fireEvent.click(screen.getByRole('checkbox', { name: /start node/i }))
    const updated = onChange.mock.calls[0][0] as typeof draft
    const nodeA = updated.nodes.find((n) => n.id === 'node-a')
    const nodeB = updated.nodes.find((n) => n.id === 'node-b')
    expect(nodeA?.isStartNode).toBe(false)
    expect(nodeB?.isStartNode).toBe(true)
  })

  it('shows delete node button for selected node', () => {
    const draft = createDefaultFlowDraft()
    render(
      <NodePropertiesPanel
        draft={draft}
        onChange={vi.fn()}
        selectedNodeId={draft.nodes[0].id}
        selectedEdgeId={null}
      />,
    )
    expect(screen.getByRole('button', { name: /delete node/i })).toBeInTheDocument()
  })

  it('removes node and its connections on delete', () => {
    const nodeId = '11111111-1111-1111-1111-111111111111'
    const otherId = '22222222-2222-2222-2222-222222222222'
    const draft = {
      name: 'Test',
      description: null,
      nodes: [
        {
          id: nodeId,
          key: 'start',
          type: 'Form' as const,
          title: 'Start',
          jsonContent: '{}',
          isStartNode: true,
        },
        {
          id: otherId,
          key: 'other',
          type: 'Form' as const,
          title: 'Other',
          jsonContent: '{}',
          isStartNode: false,
        },
      ],
      connections: [{ sourceNodeId: nodeId, targetNodeId: otherId, priority: 0 }],
    }
    const onChange = vi.fn()
    render(
      <NodePropertiesPanel
        draft={draft}
        onChange={onChange}
        selectedNodeId={nodeId}
        selectedEdgeId={null}
      />,
    )
    fireEvent.click(screen.getByRole('button', { name: /delete node/i }))
    const updated = onChange.mock.calls[0][0] as typeof draft
    expect(updated.nodes).not.toContainEqual(expect.objectContaining({ id: nodeId }))
    expect(updated.connections).toHaveLength(0)
  })

  it('shows connection properties when an edge is selected', () => {
    const draft = {
      ...createDefaultFlowDraft(),
      nodes: [
        { ...createDefaultFlowDraft().nodes[0], id: 'src' },
        {
          id: 'tgt',
          key: 'target',
          type: 'Form' as const,
          title: 'Target',
          jsonContent: '{}',
          isStartNode: false,
        },
      ],
      connections: [
        {
          sourceNodeId: 'src',
          targetNodeId: 'tgt',
          conditionField: 'country',
          conditionOperator: 'Equals',
          conditionValue: 'US',
          priority: 0,
        },
      ],
    }
    render(
      <NodePropertiesPanel
        draft={draft}
        onChange={vi.fn()}
        selectedNodeId={null}
        selectedEdgeId="src__tgt__0"
      />,
    )
    expect(screen.getByDisplayValue('country')).toBeInTheDocument()
    expect(screen.getByDisplayValue('Equals')).toBeInTheDocument()
    expect(screen.getByDisplayValue('US')).toBeInTheDocument()
  })

  it('calls onChange when connection conditionField is updated', () => {
    const draft = {
      ...createDefaultFlowDraft(),
      nodes: [
        { ...createDefaultFlowDraft().nodes[0], id: 'src' },
        {
          id: 'tgt',
          key: 'target',
          type: 'Form' as const,
          title: 'Target',
          jsonContent: '{}',
          isStartNode: false,
        },
      ],
      connections: [
        {
          sourceNodeId: 'src',
          targetNodeId: 'tgt',
          conditionField: 'country',
          conditionOperator: null,
          conditionValue: null,
          priority: 0,
        },
      ],
    }
    const onChange = vi.fn()
    render(
      <NodePropertiesPanel
        draft={draft}
        onChange={onChange}
        selectedNodeId={null}
        selectedEdgeId="src__tgt__0"
      />,
    )
    fireEvent.change(screen.getByDisplayValue('country'), { target: { value: 'region' } })
    const updated = onChange.mock.calls[0][0] as typeof draft
    expect(updated.connections[0].conditionField).toBe('region')
  })
})
