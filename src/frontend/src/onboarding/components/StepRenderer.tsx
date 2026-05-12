import { Component, type ReactNode, useState } from 'react'
import { Label } from '@radix-ui/react-label'
import type { FlowNode } from '../types/flow'

// ── Error boundary ────────────────────────────────────────────────────────────
type ErrorBoundaryState = { hasError: boolean }
class FormErrorBoundary extends Component<{ children: ReactNode }, ErrorBoundaryState> {
  state: ErrorBoundaryState = { hasError: false }

  static getDerivedStateFromError(): ErrorBoundaryState {
    return { hasError: true }
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="rounded border border-red-200 bg-red-50 p-4 text-sm text-red-700">
          This step could not be loaded. Please try refreshing the page.
        </div>
      )
    }
    return this.props.children
  }
}

// ── Field schema ──────────────────────────────────────────────────────────────
type FieldType = 'text' | 'email' | 'number' | 'select' | 'checkbox' | 'textarea' | 'date' | string

interface FieldSchema {
  name: string
  type: FieldType
  required?: boolean
  options?: string[]
}

interface FormSchema {
  fields?: FieldSchema[]
}

function parseFormSchema(jsonContent: string): FormSchema | null {
  try {
    return JSON.parse(jsonContent) as FormSchema
  } catch {
    return null
  }
}

// ── Individual field renderer ─────────────────────────────────────────────────
const inputClass =
  'w-full rounded border border-slate-300 px-3 py-2 text-sm focus:outline-none focus:ring-1 focus:ring-slate-500'

function FieldInput({
  field,
  value,
  onChange,
}: {
  field: FieldSchema
  value: unknown
  onChange: (val: unknown) => void
}) {
  switch (field.type) {
    case 'select':
      return (
        <select
          id={field.name}
          name={field.name}
          required={field.required}
          value={String(value ?? '')}
          onChange={(e) => onChange(e.target.value)}
          className={inputClass}
        >
          <option value="">— select —</option>
          {(field.options ?? []).map((opt) => (
            <option key={opt} value={opt}>
              {opt}
            </option>
          ))}
        </select>
      )
    case 'checkbox':
      return (
        <input
          id={field.name}
          name={field.name}
          type="checkbox"
          checked={Boolean(value)}
          onChange={(e) => onChange(e.target.checked)}
          className="h-4 w-4 rounded border-slate-300"
        />
      )
    case 'textarea':
      return (
        <textarea
          id={field.name}
          name={field.name}
          required={field.required}
          value={String(value ?? '')}
          onChange={(e) => onChange(e.target.value)}
          className={`${inputClass} min-h-[80px] resize-y`}
        />
      )
    default: {
      const htmlType = ['text', 'email', 'number', 'date'].includes(field.type as string)
        ? (field.type as string)
        : 'text'
      return (
        <input
          id={field.name}
          name={field.name}
          type={htmlType}
          required={field.required}
          value={String(value ?? '')}
          onChange={(e) => onChange(e.target.value)}
          className={inputClass}
        />
      )
    }
  }
}

// ── Dynamic form (schema-driven) ──────────────────────────────────────────────
function DynamicForm({
  node,
  schema,
  onSubmit,
}: {
  node: FlowNode
  schema: FormSchema
  onSubmit: (payload: Record<string, unknown>) => Promise<void>
}) {
  const fields = schema.fields ?? []

  const [values, setValues] = useState<Record<string, unknown>>(() =>
    Object.fromEntries(fields.map((f) => [f.name, f.type === 'checkbox' ? false : ''])),
  )

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    await onSubmit(values)
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <Label className="text-sm font-semibold text-slate-800">{node.title}</Label>
      {fields.length === 0 && (
        <p className="text-xs text-slate-500">No fields defined for this step.</p>
      )}
      {fields.map((field) => (
        <div key={field.name} className="space-y-1">
          <label htmlFor={field.name} className="block text-sm font-medium text-slate-700">
            {field.name}
            {field.required && <span className="ml-1 text-red-500">*</span>}
          </label>
          <FieldInput
            field={field}
            value={values[field.name]}
            onChange={(val) => setValues((prev) => ({ ...prev, [field.name]: val }))}
          />
        </div>
      ))}
      <button
        type="submit"
        className="rounded bg-slate-900 px-4 py-2 text-sm text-white hover:bg-slate-700 active:bg-slate-800"
      >
        Submit
      </button>
    </form>
  )
}

// ── Main renderer ─────────────────────────────────────────────────────────────
type StepRendererProps = {
  node: FlowNode | null
  onSubmit: (payload: Record<string, unknown>) => Promise<void>
}

const cardClassName = 'rounded-lg border border-slate-200 bg-white p-4 shadow-sm'

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

function FormStep({
  node,
  onSubmit,
}: {
  node: FlowNode
  onSubmit: (payload: Record<string, unknown>) => Promise<void>
}) {
  const schema = parseFormSchema(node.jsonContent)

  if (schema === null) {
    return (
      <div className="rounded border border-red-200 bg-red-50 p-4 text-sm text-red-700">
        This step could not be loaded — the form schema is invalid. Please contact support.
      </div>
    )
  }

  return <DynamicForm node={node} schema={schema} onSubmit={onSubmit} />
}

export function StepRenderer({ node, onSubmit }: StepRendererProps) {
  if (!node) {
    return <div className={cardClassName}>Journey complete 🎉</div>
  }

  const content = (() => {
    try {
      return JSON.parse(node.jsonContent) as Record<string, unknown>
    } catch {
      return {}
    }
  })()

  const renderContent = () => {
    switch (node.type) {
      case 'Form':
        return (
          <FormErrorBoundary>
            <FormStep node={node} onSubmit={onSubmit} />
          </FormErrorBoundary>
        )
      case 'DocumentUpload':
        return (
          <div className="space-y-3">
            <p className="text-sm text-slate-700">{node.title}</p>
            <button
              type="button"
              className="rounded border border-slate-300 px-3 py-2 text-sm hover:bg-slate-50"
              onClick={() => void onSubmit({ uploaded: true })}
            >
              Upload document
            </button>
          </div>
        )
      case 'Redirect':
        return (
          <div className="space-y-3">
            <p className="text-sm text-slate-700">{node.title}</p>
            <a
              className="text-sm font-semibold text-blue-600 hover:underline"
              href={getSafeRedirectUrl(content.url)}
              rel="noopener noreferrer"
            >
              Continue to external provider →
            </a>
          </div>
        )
      case 'Information':
        return <p className="text-sm text-slate-700">{node.title}</p>
      case 'Logic':
        return (
          <div className="flex items-center gap-2 text-sm text-slate-500">
            <svg
              className="h-4 w-4 animate-spin"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
            >
              <path d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83M2 12h4M18 12h4M4.93 19.07l2.83-2.83M16.24 7.76l2.83-2.83" />
            </svg>
            Processing…
          </div>
        )
      default:
        return <p className="text-sm text-slate-700">{node.title}</p>
    }
  }

  return <section className={cardClassName}>{renderContent()}</section>
}

