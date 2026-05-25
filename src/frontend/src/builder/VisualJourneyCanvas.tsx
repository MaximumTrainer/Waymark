import { useCallback, useMemo, useState } from 'react'
import ReactFlow, {
  Background,
  Controls,
  MiniMap,
  type Node,
  type Edge,
  type NodeChange,
  type EdgeChange,
  type Connection,
} from 'reactflow'
import 'reactflow/dist/style.css'
import type { FlowDraft, FlowDraftNode, FlowDraftConnection } from './flowAuthoring'
import {
  connectionToEdgeId,
  draftConnectionsToEdges,
  getNodeStyle,
} from './visualJourneyCanvasUtils'

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
  // Persisted user-driven positions only (updated via drag in onNodesChange).
  const [positions, setPositions] = useState<Map<string, { x: number; y: number }>>(
    () => new Map(),
  )

  // Computed layout positions for any node that doesn't have a persisted position.
  const layoutPositions = useMemo(
    () => computeInitialPositions(draft.nodes, draft.connections),
    [draft.nodes, draft.connections],
  )

  const rfNodes = useMemo<Node[]>(() => {
    return draft.nodes.map((node, index) => {
      const persisted = positions.get(node.id)
      const layout = layoutPositions.get(node.id)
      const position =
        persisted ??
        layout ?? { x: (index % 4) * 240, y: 50 + Math.floor(index / 4) * 120 }
      return {
        id: node.id,
        data: { label: buildNodeLabel(node) },
        position,
        style: getNodeStyle(node),
        selected: node.id === selectedNodeId,
      }
    })
  }, [draft.nodes, layoutPositions, positions, selectedNodeId])

  const rfEdges = useMemo<Edge[]>(
    () =>
      draftConnectionsToEdges(draft.connections).map((edge) => ({
        ...edge,
        selected: edge.id === selectedEdgeId,
      })),
    [draft.connections, selectedEdgeId],
  )

  const onNodesChange = useCallback(
    (changes: NodeChange[]) => {
      // Track position updates persistently so they survive re-renders.
      const positionUpdates = changes.filter(
        (c): c is NodeChange & { type: 'position'; id: string; position: { x: number; y: number } } =>
          c.type === 'position' && !!c.position,
      )

      const removedIds = new Set(
        changes.filter((c) => c.type === 'remove').map((c) => c.id),
      )

      if (positionUpdates.length > 0 || removedIds.size > 0) {
        setPositions((prev) => {
          const next = new Map(prev)
          for (const change of positionUpdates) {
            next.set(change.id, change.position)
          }
          for (const id of removedIds) {
            next.delete(id)
          }
          return next
        })
      }

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
      onChange({ ...draft, connections: [...draft.connections, newConn] })
    },
    [draft, onChange],
  )

  return (
    <div className="h-full w-full">
      <ReactFlow
        nodes={rfNodes}
        edges={rfEdges}
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
