import { useState, useCallback, useEffect, useRef, useMemo } from 'react'
import ReactFlow, {
  Background,
  Controls,
  MiniMap,
  applyNodeChanges,
  applyEdgeChanges,
  addEdge,
  type Node,
  type Edge,
  type NodeChange,
  type EdgeChange,
  type Connection,
} from 'reactflow'
import 'reactflow/dist/style.css'
import type { FlowDraft, FlowDraftNode, FlowDraftConnection } from './flowAuthoring'
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
): React.CSSProperties {
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

function computeInitialPositions(
  nodes: FlowDraftNode[],
  connections: FlowDraftConnection[],
): Map<string, { x: number; y: number }> {
  if (nodes.length === 0) return new Map()

  const startNode = nodes.find((n) => n.isStartNode) ?? nodes[0]
  const adjacency = new Map<string, string[]>()
  for (const n of nodes) adjacency.set(n.id, [])
  for (const conn of connections) {
    adjacency.get(conn.sourceNodeId)?.push(conn.targetNodeId)
  }

  const positions = new Map<string, { x: number; y: number }>()
  const visited = new Set<string>()
  const queue: Array<{ id: string; depth: number }> = [{ id: startNode.id, depth: 0 }]
  const depthCounters = new Map<number, number>()

  while (queue.length > 0) {
    const item = queue.shift()!
    if (visited.has(item.id)) continue
    visited.add(item.id)
    const yIndex = depthCounters.get(item.depth) ?? 0
    depthCounters.set(item.depth, yIndex + 1)
    positions.set(item.id, { x: item.depth * 220, y: yIndex * 100 })
    for (const childId of adjacency.get(item.id) ?? []) {
      if (!visited.has(childId)) queue.push({ id: childId, depth: item.depth + 1 })
    }
  }

  let fallbackY = (Math.max(0, ...[...depthCounters.values()]) + 1) * 100
  for (const n of nodes) {
    if (!positions.has(n.id)) {
      positions.set(n.id, { x: 0, y: fallbackY })
      fallbackY += 100
    }
  }

  return positions
}

function buildNodeLabel(node: FlowDraftNode): string {
  return `${node.isStartNode ? '⭐ ' : ''}${node.title} [${node.type}]`
}

function draftNodesToRfNodes(
  nodes: FlowDraftNode[],
  positions: Map<string, { x: number; y: number }>,
  existingById: Map<string, Node>,
): Node[] {
  return nodes.map((node, index) => {
    const existing = existingById.get(node.id)
    const position =
      existing?.position ??
      positions.get(node.id) ??
      { x: (index % 4) * 240, y: 50 + Math.floor(index / 4) * 120 }
    return {
      id: node.id,
      data: { label: buildNodeLabel(node) },
      position,
      style: getNodeStyle(node),
      selected: existing?.selected ?? false,
    }
  })
}

type VisualJourneyCanvasProps = {
  draft: FlowDraft
  onChange: (draft: FlowDraft) => void
  selectedNodeId: string | null
  selectedEdgeId: string | null
  onSelectNode: (id: string | null) => void
  onSelectEdge: (id: string | null) => void
}

export function VisualJourneyCanvas({
  draft,
  onChange,
  selectedNodeId,
  selectedEdgeId,
  onSelectNode,
  onSelectEdge,
}: VisualJourneyCanvasProps) {
  const positionsRef = useRef<Map<string, { x: number; y: number }>>(
    computeInitialPositions(draft.nodes, draft.connections),
  )

  const [rfNodes, setRfNodes] = useState<Node[]>(() =>
    draftNodesToRfNodes(draft.nodes, positionsRef.current, new Map()),
  )
  const [rfEdges, setRfEdges] = useState<Edge[]>(() =>
    draftConnectionsToEdges(draft.connections),
  )

  // Sync when draft changes (e.g., from properties panel updates or node additions)
  const prevDraftRef = useRef(draft)
  useEffect(() => {
    if (draft === prevDraftRef.current) return
    prevDraftRef.current = draft

    setRfNodes((prev) => {
      const existingById = new Map(prev.map((n) => [n.id, n]))
      return draftNodesToRfNodes(draft.nodes, positionsRef.current, existingById)
    })
    setRfEdges(
      draftConnectionsToEdges(draft.connections).map((edge) => ({
        ...edge,
        selected: edge.id === selectedEdgeId,
      })),
    )
  }, [draft, selectedEdgeId])

  // Keep selected state in sync with external selection
  useEffect(() => {
    setRfNodes((prev) =>
      prev.map((n) => ({ ...n, selected: n.id === selectedNodeId })),
    )
  }, [selectedNodeId])

  useEffect(() => {
    setRfEdges((prev) =>
      prev.map((e) => ({ ...e, selected: e.id === selectedEdgeId })),
    )
  }, [selectedEdgeId])

  const onNodesChange = useCallback(
    (changes: NodeChange[]) => {
      // Track position updates persistently
      for (const change of changes) {
        if (change.type === 'position' && change.position) {
          positionsRef.current.set(change.id, change.position)
        }
      }

      setRfNodes((nds) => applyNodeChanges(changes, nds))

      const removedIds = new Set(
        changes.filter((c) => c.type === 'remove').map((c) => c.id),
      )
      if (removedIds.size > 0) {
        onChange({
          ...draft,
          nodes: draft.nodes.filter((n) => !removedIds.has(n.id)),
          connections: draft.connections.filter(
            (c) => !removedIds.has(c.sourceNodeId) && !removedIds.has(c.targetNodeId),
          ),
        })
      }
    },
    [draft, onChange],
  )

  const onEdgesChange = useCallback(
    (changes: EdgeChange[]) => {
      setRfEdges((eds) => applyEdgeChanges(changes, eds))

      const removedIds = new Set(
        changes.filter((c) => c.type === 'remove').map((c) => c.id),
      )
      if (removedIds.size > 0) {
        onChange({
          ...draft,
          connections: draft.connections.filter(
            (c) => !removedIds.has(connectionToEdgeId(c)),
          ),
        })
      }
    },
    [draft, onChange],
  )

  const onConnect = useCallback(
    (connection: Connection) => {
      if (!connection.source || !connection.target) return
      const newConn: FlowDraftConnection = {
        sourceNodeId: connection.source,
        targetNodeId: connection.target,
        priority: draft.connections.filter(
          (c) => c.sourceNodeId === connection.source && c.targetNodeId === connection.target,
        ).length,
      }
      const newEdge = {
        id: connectionToEdgeId(newConn),
        source: newConn.sourceNodeId,
        target: newConn.targetNodeId,
      }
      setRfEdges((eds) => addEdge(newEdge, eds))
      onChange({ ...draft, connections: [...draft.connections, newConn] })
    },
    [draft, onChange],
  )

  const stableNodes = useMemo(() => rfNodes, [rfNodes])
  const stableEdges = useMemo(() => rfEdges, [rfEdges])

  return (
    <div className="h-full w-full">
      <ReactFlow
        nodes={stableNodes}
        edges={stableEdges}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onConnect={onConnect}
        onNodeClick={(_, node) => {
          onSelectNode(node.id)
          onSelectEdge(null)
        }}
        onEdgeClick={(_, edge) => {
          onSelectEdge(edge.id)
          onSelectNode(null)
        }}
        onPaneClick={() => {
          onSelectNode(null)
          onSelectEdge(null)
        }}
        fitView
        deleteKeyCode="Delete"
      >
        <Background />
        <Controls />
        <MiniMap />
      </ReactFlow>
    </div>
  )
}
