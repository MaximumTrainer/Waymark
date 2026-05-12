import { useMemo } from 'react'
import ReactFlow, { Background, Controls, MiniMap, type Node, type Edge } from 'reactflow'
import 'reactflow/dist/style.css'
import { useFlow } from '../onboarding/hooks/useFlow'
import type { FlowConnection, FlowNodeDetail, FlowDefinition } from '../onboarding/types/flow'

type JourneyBuilderProps = {
  flowId: string | null
  currentNodeId?: string | null
  visitedNodeIds?: ReadonlySet<string>
  isCompleted?: boolean
}

// Assign x/y positions via BFS from the start node
function computePositions(flow: FlowDefinition): Map<string, { x: number; y: number }> {
  const startNode = flow.nodes.find((n) => n.isStartNode) ?? flow.nodes[0]
  if (!startNode) return new Map()

  const adjacency = new Map<string, string[]>()
  for (const n of flow.nodes) adjacency.set(n.id, [])
  for (const conn of flow.connections) {
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
    positions.set(item.id, { x: item.depth * 220, y: yIndex * 90 })

    for (const childId of adjacency.get(item.id) ?? []) {
      if (!visited.has(childId)) queue.push({ id: childId, depth: item.depth + 1 })
    }
  }

  // Position any unreachable nodes at the bottom
  let fallbackY = (Math.max(0, ...[...depthCounters.values()]) + 1) * 90
  for (const n of flow.nodes) {
    if (!positions.has(n.id)) {
      positions.set(n.id, { x: 0, y: fallbackY })
      fallbackY += 90
    }
  }

  return positions
}

function buildReactFlowNodes(
  flowNodes: FlowNodeDetail[],
  positions: Map<string, { x: number; y: number }>,
  currentNodeId: string | null | undefined,
  visitedNodeIds: ReadonlySet<string>,
): Node[] {
  return flowNodes.map((n) => {
    const isCurrent = n.id === currentNodeId
    const isVisited = visitedNodeIds.has(n.id)
    const style = isCurrent
      ? { background: '#1e40af', color: '#fff', borderColor: '#1e3a8a', fontWeight: 600 }
      : isVisited
        ? { background: '#dcfce7', borderColor: '#16a34a' }
        : { background: '#f8fafc', borderColor: '#cbd5e1' }

    return {
      id: n.id,
      data: { label: n.title },
      position: positions.get(n.id) ?? { x: 0, y: 0 },
      style: { fontSize: 11, borderRadius: 6, padding: '4px 8px', ...style },
    }
  })
}

function buildReactFlowEdges(connections: FlowConnection[]): Edge[] {
  return connections.map((c) => {
    const parts = [c.conditionField, c.conditionOperator, c.conditionValue].filter(Boolean)
    return {
      id: c.id,
      source: c.sourceNodeId,
      target: c.targetNodeId,
      label: parts.length > 0 ? parts.join(' ') : undefined,
      style: { fontSize: 10 },
    }
  })
}

// Fallback when flow API is unavailable
const FALLBACK_NODES: Node[] = [
  { id: '1', data: { label: 'Country Form' }, position: { x: 80, y: 40 } },
  { id: '2', data: { label: 'SSN Form' }, position: { x: 320, y: -10 } },
  { id: '3', data: { label: 'Passport Upload' }, position: { x: 320, y: 90 } },
]
const FALLBACK_EDGES: Edge[] = [
  { id: 'e1-2', source: '1', target: '2', label: 'Country == USA' },
  { id: 'e1-3', source: '1', target: '3', label: 'Country != USA' },
]

export function JourneyBuilder({
  flowId,
  currentNodeId,
  visitedNodeIds = new Set(),
  isCompleted = false,
}: JourneyBuilderProps) {
  const { flow, isLoading, error } = useFlow(flowId)

  const { nodes, edges } = useMemo(() => {
    if (!flow) return { nodes: FALLBACK_NODES, edges: FALLBACK_EDGES }
    const positions = computePositions(flow)
    return {
      nodes: buildReactFlowNodes(flow.nodes, positions, currentNodeId, visitedNodeIds),
      edges: buildReactFlowEdges(flow.connections),
    }
  }, [flow, currentNodeId, visitedNodeIds])

  return (
    <div className="space-y-2">
      {isCompleted && (
        <div className="rounded-md bg-green-50 px-4 py-2 text-sm font-medium text-green-700 border border-green-200">
          🎉 Journey complete — all steps finished.
        </div>
      )}
      {isLoading && (
        <p className="text-xs text-slate-500">Loading flow diagram…</p>
      )}
      {error && !flow && (
        <p className="text-xs text-amber-600">Using sample data — could not load live flow.</p>
      )}
      <div className="h-72 overflow-hidden rounded-lg border border-slate-200 bg-white">
        <ReactFlow fitView nodes={nodes} edges={edges}>
          <MiniMap />
          <Controls />
          <Background />
        </ReactFlow>
      </div>
    </div>
  )
}

