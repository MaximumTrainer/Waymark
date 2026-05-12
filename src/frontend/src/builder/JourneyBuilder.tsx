import ReactFlow, { Background, Controls, MiniMap } from 'reactflow'
import 'reactflow/dist/style.css'

const nodes = [
  { id: '1', data: { label: 'Country Form' }, position: { x: 80, y: 40 } },
  { id: '2', data: { label: 'SSN Form' }, position: { x: 320, y: -10 } },
  { id: '3', data: { label: 'Passport Upload' }, position: { x: 320, y: 90 } },
]

const edges = [
  { id: 'e1-2', source: '1', target: '2', label: 'Country == USA' },
  { id: 'e1-3', source: '1', target: '3', label: 'Country != USA' },
]

export function JourneyBuilder() {
  return (
    <div className="h-72 overflow-hidden rounded-lg border border-slate-200 bg-white">
      <ReactFlow fitView nodes={nodes} edges={edges}>
        <MiniMap />
        <Controls />
        <Background />
      </ReactFlow>
    </div>
  )
}
