import type { ReactNode } from 'react'
import { Label } from '@radix-ui/react-label'
import { Slot } from '@radix-ui/react-slot'
import type { FlowNode } from '../types/flow'

type StepRendererProps = {
  node: FlowNode | null
  onSubmit: (payload: Record<string, unknown>) => Promise<void>
}

const cardClassName = 'rounded-lg border border-slate-200 bg-white p-4 shadow-sm'

function parseContent(node: FlowNode | null): Record<string, unknown> {
  if (!node) {
    return {}
  }

  try {
    return JSON.parse(node.jsonContent) as Record<string, unknown>
  } catch {
    return {}
  }
}

function getSafeRedirectUrl(value: unknown): string {
  if (typeof value !== 'string' || value.trim().length === 0) {
    return '#'
  }

  const trimmed = value.trim()
  if (trimmed.startsWith('/')) {
    return trimmed
  }

  try {
    const parsed = new URL(trimmed)
    return parsed.protocol === 'http:' || parsed.protocol === 'https:' ? parsed.toString() : '#'
  } catch {
    return '#'
  }
}

export function StepRenderer({ node, onSubmit }: StepRendererProps) {
  if (!node) {
    return <div className={cardClassName}>Journey complete 🎉</div>
  }

  const content = parseContent(node)

  const variants: Record<FlowNode['type'], ReactNode> = {
    Form: (
      <form
        className="space-y-3"
        onSubmit={(event) => {
          event.preventDefault()
          onSubmit(content)
        }}
      >
        <div>
          <Label className="text-sm font-medium text-slate-700">{node.title}</Label>
          <p className="mt-1 text-xs text-slate-500">Schema-driven forms can be rendered from jsonContent metadata.</p>
        </div>
        <button type="submit" className="rounded bg-slate-900 px-3 py-2 text-sm text-white">Submit form</button>
      </form>
    ),
    DocumentUpload: (
      <div className="space-y-3">
        <p className="text-sm text-slate-700">{node.title}</p>
        <Slot>
          <button
            type="button"
            className="rounded border border-slate-300 px-3 py-2 text-sm"
            onClick={() => onSubmit({ uploaded: true })}
          >
            Upload document
          </button>
        </Slot>
      </div>
    ),
    Redirect: (
      <div className="space-y-3">
        <p className="text-sm text-slate-700">{node.title}</p>
        <a className="text-sm font-semibold text-blue-600" href={getSafeRedirectUrl(content.url)}>
          Continue to external provider
        </a>
      </div>
    ),
    Information: <p className="text-sm text-slate-700">{node.title}</p>,
    Logic: <p className="text-sm text-slate-700">Branching logic node: {node.key}</p>,
  }

  return <section className={cardClassName}>{variants[node.type]}</section>
}
