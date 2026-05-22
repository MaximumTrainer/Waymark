import type { NodeType } from '../onboarding/types/flow'
import type { FlowDraft, FlowDraftConnection, FlowDraftNode } from './flowAuthoring'
import { connectionToEdgeId } from './VisualJourneyCanvas'

const NODE_TYPES: NodeType[] = ['Form', 'DocumentUpload', 'Redirect', 'Information', 'Logic']

type NodePropertiesPanelProps = {
  draft: FlowDraft
  onChange: (draft: FlowDraft) => void
  selectedNodeId: string | null
  selectedEdgeId: string | null
}

export function NodePropertiesPanel({
  draft,
  onChange,
  selectedNodeId,
  selectedEdgeId,
}: NodePropertiesPanelProps) {
  const selectedNode = draft.nodes.find((n) => n.id === selectedNodeId) ?? null
  const selectedEdge = selectedEdgeId
    ? (draft.connections.find((c) => connectionToEdgeId(c) === selectedEdgeId) ?? null)
    : null

  const updateNode = (updates: Partial<FlowDraftNode>) => {
    if (!selectedNode) return
    const updated = draft.nodes.map((n) =>
      n.id === selectedNode.id ? { ...n, ...updates } : n,
    )
    if (updates.isStartNode === true) {
      // Enforce exactly one start node
      onChange({
        ...draft,
        nodes: updated.map((n) =>
          n.id === selectedNode.id ? n : { ...n, isStartNode: false },
        ),
      })
    } else {
      onChange({ ...draft, nodes: updated })
    }
  }

  const updateConnection = (updates: Partial<FlowDraftConnection>) => {
    if (!selectedEdge) return
    const oldId = connectionToEdgeId(selectedEdge)
    onChange({
      ...draft,
      connections: draft.connections.map((c) =>
        connectionToEdgeId(c) === oldId ? { ...c, ...updates } : c,
      ),
    })
  }

  const deleteNode = () => {
    if (!selectedNode) return
    onChange({
      ...draft,
      nodes: draft.nodes.filter((n) => n.id !== selectedNode.id),
      connections: draft.connections.filter(
        (c) => c.sourceNodeId !== selectedNode.id && c.targetNodeId !== selectedNode.id,
      ),
    })
  }

  if (!selectedNode && !selectedEdge) {
    return (
      <aside className="w-80 shrink-0 rounded-lg border border-slate-200 bg-white p-4 text-sm text-slate-500">
        <p>Select a node or edge to edit its properties.</p>
      </aside>
    )
  }

  if (selectedNode) {
    return (
      <aside className="w-80 shrink-0 overflow-y-auto rounded-lg border border-slate-200 bg-white p-4">
        <h3 className="mb-3 font-semibold text-slate-900">Node Properties</h3>
        <div className="space-y-3">
          <label className="block text-sm text-slate-700">
            Title
            <input
              className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              value={selectedNode.title}
              onChange={(e) => updateNode({ title: e.target.value })}
            />
          </label>

          <label className="block text-sm text-slate-700">
            Key
            <input
              className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              value={selectedNode.key}
              onChange={(e) => updateNode({ key: e.target.value })}
            />
          </label>

          <label className="block text-sm text-slate-700">
            Type
            <select
              className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              value={selectedNode.type}
              onChange={(e) => updateNode({ type: e.target.value as NodeType })}
            >
              {NODE_TYPES.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
          </label>

          <label className="flex items-center gap-2 text-sm text-slate-700">
            <input
              type="checkbox"
              checked={selectedNode.isStartNode}
              onChange={(e) => updateNode({ isStartNode: e.target.checked })}
            />
            Start node
          </label>

          <label className="block text-sm text-slate-700">
            JSON Content
            <textarea
              className="mt-1 h-24 w-full rounded border border-slate-300 px-3 py-2 text-xs font-mono"
              value={selectedNode.jsonContent}
              onChange={(e) => updateNode({ jsonContent: e.target.value })}
            />
          </label>

          <label className="block text-sm text-slate-700">
            Compliance Rule JSON (optional)
            <textarea
              className="mt-1 h-20 w-full rounded border border-slate-300 px-3 py-2 text-xs font-mono"
              value={selectedNode.complianceRuleJson ?? ''}
              onChange={(e) =>
                updateNode({ complianceRuleJson: e.target.value || null })
              }
            />
          </label>

          <p className="truncate text-xs text-slate-400">ID: {selectedNode.id}</p>

          <button
            type="button"
            onClick={deleteNode}
            className="w-full rounded border border-rose-300 bg-white px-3 py-2 text-sm text-rose-700 hover:bg-rose-50"
          >
            Delete node
          </button>
        </div>
      </aside>
    )
  }

  return (
    <aside className="w-80 shrink-0 overflow-y-auto rounded-lg border border-slate-200 bg-white p-4">
      <h3 className="mb-3 font-semibold text-slate-900">Connection Properties</h3>
      {selectedEdge && (
        <div className="space-y-3">
          <label className="block text-sm text-slate-700">
            Condition Field
            <input
              className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              value={selectedEdge.conditionField ?? ''}
              onChange={(e) =>
                updateConnection({ conditionField: e.target.value || null })
              }
            />
          </label>

          <label className="block text-sm text-slate-700">
            Condition Operator
            <input
              className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              value={selectedEdge.conditionOperator ?? ''}
              onChange={(e) =>
                updateConnection({ conditionOperator: e.target.value || null })
              }
            />
          </label>

          <label className="block text-sm text-slate-700">
            Condition Value
            <input
              className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              value={selectedEdge.conditionValue ?? ''}
              onChange={(e) =>
                updateConnection({ conditionValue: e.target.value || null })
              }
            />
          </label>

          <label className="block text-sm text-slate-700">
            Priority
            <input
              type="number"
              className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              value={selectedEdge.priority}
              min={0}
              onChange={(e) =>
                updateConnection({ priority: parseInt(e.target.value, 10) || 0 })
              }
            />
          </label>

          <p className="truncate text-xs text-slate-400">
            {selectedEdge.sourceNodeId} → {selectedEdge.targetNodeId}
          </p>
        </div>
      )}
    </aside>
  )
}
