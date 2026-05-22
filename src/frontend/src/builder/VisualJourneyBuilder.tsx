import { useState } from 'react'
import type { FlowDefinition, NodeType } from '../onboarding/types/flow'
import {
  buildFlowWritePayload,
  createDefaultFlowDraft,
  toFlowDraft,
  validateFlowDraft,
  type FlowDraft,
  type FlowDraftNode,
} from './flowAuthoring'
import { NodePropertiesPanel } from './NodePropertiesPanel'
import { VisualJourneyCanvas } from './VisualJourneyCanvas'

const NODE_TYPES: NodeType[] = ['Form', 'DocumentUpload', 'Redirect', 'Information', 'Logic']

type StatusState = { kind: 'idle' | 'success' | 'error'; message: string }

type VisualJourneyBuilderProps = {
  onLoad: (flowId: string) => Promise<FlowDefinition>
  onSave: (flowId: string, draft: FlowDraft) => Promise<FlowDefinition>
  onCreateNew: (draft: FlowDraft) => Promise<FlowDefinition>
}

export function VisualJourneyBuilder({ onLoad, onSave, onCreateNew }: VisualJourneyBuilderProps) {
  const [draft, setDraft] = useState<FlowDraft>(createDefaultFlowDraft)
  const [flowId, setFlowId] = useState('')
  const [currentVersion, setCurrentVersion] = useState<number | null>(null)
  const [status, setStatus] = useState<StatusState>({ kind: 'idle', message: '' })
  const [isBusy, setIsBusy] = useState(false)
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)
  const [selectedEdgeId, setSelectedEdgeId] = useState<string | null>(null)

  const validationErrors = validateFlowDraft(draft)

  const applyLoadedFlow = (flow: FlowDefinition) => {
    setFlowId(flow.id)
    setDraft(toFlowDraft(flow))
    setCurrentVersion(flow.version)
    setSelectedNodeId(null)
    setSelectedEdgeId(null)
  }

  const handleLoad = async () => {
    if (!flowId.trim()) {
      setStatus({ kind: 'error', message: 'Enter a flow ID before loading.' })
      return
    }
    setIsBusy(true)
    setStatus({ kind: 'idle', message: '' })
    try {
      const flow = await onLoad(flowId.trim())
      applyLoadedFlow(flow)
      setStatus({
        kind: 'success',
        message: `Loaded flow ${flow.id} (version ${flow.version}).`,
      })
    } catch (err) {
      setStatus({
        kind: 'error',
        message: err instanceof Error ? err.message : 'Failed to load flow.',
      })
    } finally {
      setIsBusy(false)
    }
  }

  const handleSaveNewVersion = async () => {
    if (validationErrors.length > 0) {
      setStatus({ kind: 'error', message: validationErrors[0] })
      return
    }
    if (!flowId.trim()) {
      setStatus({ kind: 'error', message: 'Enter a flow ID before saving a new version.' })
      return
    }
    setIsBusy(true)
    setStatus({ kind: 'idle', message: '' })
    try {
      const flow = await onSave(flowId.trim(), buildFlowWritePayload(draft))
      applyLoadedFlow(flow)
      setStatus({
        kind: 'success',
        message: `Saved flow ${flow.id} as version ${flow.version}.`,
      })
    } catch (err) {
      setStatus({
        kind: 'error',
        message: err instanceof Error ? err.message : 'Failed to save flow.',
      })
    } finally {
      setIsBusy(false)
    }
  }

  const handleCreateNew = async () => {
    if (validationErrors.length > 0) {
      setStatus({ kind: 'error', message: validationErrors[0] })
      return
    }
    setIsBusy(true)
    setStatus({ kind: 'idle', message: '' })
    try {
      const flow = await onCreateNew(buildFlowWritePayload(draft))
      applyLoadedFlow(flow)
      setStatus({
        kind: 'success',
        message: `Created flow ${flow.id} (version ${flow.version}).`,
      })
    } catch (err) {
      setStatus({
        kind: 'error',
        message: err instanceof Error ? err.message : 'Failed to create flow.',
      })
    } finally {
      setIsBusy(false)
    }
  }

  const handleReset = () => {
    setDraft(createDefaultFlowDraft())
    setFlowId('')
    setCurrentVersion(null)
    setStatus({ kind: 'idle', message: '' })
    setSelectedNodeId(null)
    setSelectedEdgeId(null)
  }

  const addNode = (type: NodeType) => {
    const newNode: FlowDraftNode = {
      id: crypto.randomUUID(),
      key: `node-${draft.nodes.length + 1}`,
      type,
      title: `New ${type}`,
      jsonContent: '{}',
      isStartNode: draft.nodes.length === 0,
    }
    setDraft((prev) => ({ ...prev, nodes: [...prev.nodes, newNode] }))
  }

  const statusClass =
    status.kind === 'success'
      ? 'rounded border border-emerald-200 bg-emerald-50 p-2 text-sm text-emerald-700'
      : status.kind === 'error'
        ? 'rounded border border-rose-200 bg-rose-50 p-2 text-sm text-rose-700'
        : ''

  return (
    <div className="flex flex-col gap-4">
      {/* Metadata row */}
      <div className="flex flex-wrap gap-3 rounded-lg border border-slate-200 bg-white p-4">
        <label className="flex flex-col gap-1 text-sm text-slate-700">
          Flow ID
          <input
            className="rounded border border-slate-300 px-3 py-2 text-sm"
            value={flowId}
            onChange={(e) => setFlowId(e.target.value)}
            placeholder="00000000-0000-0000-0000-000000000000"
          />
        </label>
        <label className="flex flex-1 flex-col gap-1 text-sm text-slate-700">
          Flow Name
          <input
            className="rounded border border-slate-300 px-3 py-2 text-sm"
            value={draft.name}
            onChange={(e) => setDraft((prev) => ({ ...prev, name: e.target.value }))}
          />
        </label>
        <label className="flex flex-1 flex-col gap-1 text-sm text-slate-700">
          Description
          <input
            className="rounded border border-slate-300 px-3 py-2 text-sm"
            value={draft.description ?? ''}
            onChange={(e) =>
              setDraft((prev) => ({ ...prev, description: e.target.value || null }))
            }
          />
        </label>
        {currentVersion !== null && (
          <div className="flex items-end pb-2 text-sm text-slate-500">v{currentVersion}</div>
        )}
      </div>

      {/* Action buttons */}
      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          disabled={isBusy}
          onClick={() => void handleLoad()}
          className="rounded border border-slate-300 bg-white px-3 py-2 text-sm text-slate-700 disabled:opacity-50"
        >
          Load
        </button>
        <button
          type="button"
          disabled={isBusy}
          onClick={() => void handleCreateNew()}
          className="rounded bg-slate-900 px-3 py-2 text-sm text-white disabled:opacity-50"
        >
          Create New
        </button>
        <button
          type="button"
          disabled={isBusy}
          onClick={() => void handleSaveNewVersion()}
          className="rounded bg-slate-700 px-3 py-2 text-sm text-white disabled:opacity-50"
        >
          Save New Version
        </button>
        <button
          type="button"
          onClick={handleReset}
          className="rounded border border-slate-300 bg-white px-3 py-2 text-sm text-slate-700 hover:bg-slate-50"
        >
          Reset
        </button>
      </div>

      {/* Validation errors */}
      {validationErrors.length > 0 && (
        <ul className="rounded border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
          {validationErrors.map((err) => (
            <li key={err}>⚠ {err}</li>
          ))}
        </ul>
      )}

      {/* Status */}
      {status.message && (
        <p
          className={statusClass}
          role={status.kind === 'error' ? 'alert' : undefined}
          aria-live={status.kind === 'error' ? 'assertive' : 'polite'}
        >
          {status.message}
        </p>
      )}

      {/* Node palette */}
      <div className="flex flex-wrap items-center gap-2 rounded-lg border border-slate-200 bg-white p-3">
        <span className="text-sm font-medium text-slate-600">Add node:</span>
        {NODE_TYPES.map((type) => (
          <button
            key={type}
            type="button"
            onClick={() => addNode(type)}
            className="rounded border border-slate-300 bg-white px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50"
          >
            + {type}
          </button>
        ))}
      </div>

      {/* Canvas + Properties panel */}
      <div className="flex gap-4" style={{ height: 600 }}>
        <div className="min-w-0 flex-1 overflow-hidden rounded-lg border border-slate-200">
          <VisualJourneyCanvas
            draft={draft}
            onChange={setDraft}
            selectedNodeId={selectedNodeId}
            selectedEdgeId={selectedEdgeId}
            onSelectNode={(id) => {
              setSelectedNodeId(id)
              if (id) setSelectedEdgeId(null)
            }}
            onSelectEdge={(id) => {
              setSelectedEdgeId(id)
              if (id) setSelectedNodeId(null)
            }}
          />
        </div>
        <NodePropertiesPanel
          draft={draft}
          onChange={setDraft}
          selectedNodeId={selectedNodeId}
          selectedEdgeId={selectedEdgeId}
        />
      </div>
    </div>
  )
}
