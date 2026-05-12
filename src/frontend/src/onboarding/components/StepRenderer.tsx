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

// ── Document upload step ───────────────────────────────────────────────────────
interface UploadedFileRef {
  fileId: string
  fileName: string
}

function DocumentUploadStep({
  node,
  sessionId,
  nodeId,
  onSubmit,
}: {
  node: FlowNode
  sessionId?: string
  nodeId?: string
  onSubmit: (payload: Record<string, unknown>) => Promise<void>
}) {
  const content = (() => {
    try {
      return JSON.parse(node.jsonContent) as Record<string, unknown>
    } catch {
      return {}
    }
  })()

  const acceptedFileTypes = Array.isArray(content.acceptedFileTypes)
    ? (content.acceptedFileTypes as string[]).join(',')
    : undefined
  const maxFiles =
    typeof content.maxFiles === 'number' ? content.maxFiles : undefined

  const [uploadError, setUploadError] = useState<string | null>(null)
  const [uploadProgress, setUploadProgress] = useState<number | null>(null)
  const [uploadedFiles, setUploadedFiles] = useState<UploadedFileRef[]>([])
  const [isUploading, setIsUploading] = useState(false)

  const uploadFiles = (fileList: FileList) => {
    if (!sessionId || !nodeId) return

    const formData = new FormData()
    for (const file of fileList) {
      formData.append('files', file)
    }

    setIsUploading(true)
    setUploadError(null)
    setUploadProgress(0)

    const xhr = new XMLHttpRequest()
    xhr.open('POST', `/api/workflow/sessions/${sessionId}/steps/${nodeId}/documents`)

    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable) {
        setUploadProgress(Math.round((e.loaded / e.total) * 100))
      }
    }

    xhr.onload = () => {
      setIsUploading(false)
      if (xhr.status >= 200 && xhr.status < 300) {
        const stored = JSON.parse(xhr.responseText) as Array<{ fileId: string; fileName: string }>
        setUploadedFiles(stored.map((f) => ({ fileId: f.fileId, fileName: f.fileName })))
        setUploadProgress(100)
      } else {
        let msg = `Upload failed (${xhr.status})`
        try {
          const problem = JSON.parse(xhr.responseText) as { title?: string }
          if (problem.title) msg = problem.title
        } catch { /* ignore */ }
        setUploadError(msg)
        setUploadProgress(null)
      }
    }

    xhr.onerror = () => {
      setIsUploading(false)
      setUploadError('Network error during upload. Please retry.')
      setUploadProgress(null)
    }

    xhr.send(formData)
  }

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files
    if (files && files.length > 0) {
      uploadFiles(files)
    }
  }

  const handleSubmit = async () => {
    await onSubmit({ files: uploadedFiles })
  }

  return (
    <div className="space-y-3">
      <p className="text-sm font-semibold text-slate-800">{node.title}</p>
      <input
        type="file"
        accept={acceptedFileTypes}
        multiple={maxFiles === undefined || maxFiles > 1}
        onChange={handleFileChange}
        disabled={isUploading}
        className="block w-full text-sm text-slate-700 file:mr-3 file:rounded file:border file:border-slate-300 file:bg-white file:px-3 file:py-1 file:text-sm file:text-slate-700 hover:file:bg-slate-50"
      />
      {uploadProgress !== null && (
        <div className="h-2 w-full overflow-hidden rounded-full bg-slate-200">
          <div
            className="h-2 rounded-full bg-slate-800 transition-all"
            style={{ width: `${uploadProgress}%` }}
          />
        </div>
      )}
      {uploadError && (
        <div className="rounded border border-red-200 bg-red-50 p-2 text-sm text-red-700">
          {uploadError}
          <button
            type="button"
            className="ml-2 underline"
            onClick={() => {
              setUploadError(null)
              setUploadProgress(null)
            }}
          >
            Retry
          </button>
        </div>
      )}
      {uploadedFiles.length > 0 && (
        <ul className="space-y-1 text-sm text-slate-700">
          {uploadedFiles.map((f) => (
            <li key={f.fileId}>✓ {f.fileName}</li>
          ))}
        </ul>
      )}
      <button
        type="button"
        disabled={uploadedFiles.length === 0 || isUploading}
        onClick={() => void handleSubmit()}
        className="rounded bg-slate-900 px-4 py-2 text-sm text-white hover:bg-slate-700 active:bg-slate-800 disabled:opacity-50"
      >
        Continue
      </button>
    </div>
  )
}

// ── Main renderer ─────────────────────────────────────────────────────────────
type StepRendererProps = {
  node: FlowNode | null
  sessionId?: string
  nodeId?: string
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

export function StepRenderer({ node, sessionId, nodeId, onSubmit }: StepRendererProps) {
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
          <FormErrorBoundary>
            <DocumentUploadStep node={node} sessionId={sessionId} nodeId={nodeId} onSubmit={onSubmit} />
          </FormErrorBoundary>
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

