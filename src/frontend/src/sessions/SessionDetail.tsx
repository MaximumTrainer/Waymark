import { useEffect, useState } from 'react'

interface Submission {
  id: string
  nodeId: string
  nodeTitle: string
  payload: Record<string, unknown>
  submittedAt: string
}

interface SessionDetailProps {
  sessionId: string
  apiKey?: string
  onBack: () => void
}

function buildHeaders(apiKey?: string): Record<string, string> {
  const h: Record<string, string> = {}
  if (apiKey) h['X-Api-Key'] = apiKey
  return h
}

function payloadSummary(payload: Record<string, unknown>): string {
  const entries = Object.entries(payload)
  if (entries.length === 0) return '(empty)'
  return entries
    .slice(0, 3)
    .map(([k, v]) => `${k}: ${String(v)}`)
    .join(', ') + (entries.length > 3 ? ', …' : '')
}

export function SessionDetail({ sessionId, apiKey, onBack }: SessionDetailProps) {
  const [submissions, setSubmissions] = useState<Submission[]>([])
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()

    const load = async () => {
      setIsLoading(true)
      setError(null)
      try {
        const res = await fetch(
          `/api/workflow/sessions/${sessionId}/submissions`,
          { headers: buildHeaders(apiKey), signal: controller.signal },
        )
        if (!res.ok) throw new Error(`Failed to load submissions (${res.status})`)
        setSubmissions((await res.json()) as Submission[])
      } catch (err) {
        if (err instanceof Error && err.name !== 'AbortError') {
          setError(err.message)
        }
      } finally {
        setIsLoading(false)
      }
    }

    void load()
    return () => controller.abort()
  }, [sessionId, apiKey])

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-3">
        <button
          onClick={onBack}
          className="rounded border border-slate-300 px-3 py-1 text-xs text-slate-700 hover:bg-slate-50"
        >
          ← Back
        </button>
        <h3 className="text-base font-semibold text-slate-800 font-mono text-sm">{sessionId}</h3>
      </div>

      {error && <p className="rounded bg-rose-50 p-3 text-sm text-rose-600">{error}</p>}
      {isLoading && <p className="text-sm text-slate-500">Loading submissions…</p>}

      {!isLoading && submissions.length === 0 && (
        <p className="text-sm text-slate-400 italic">No submissions recorded for this session.</p>
      )}

      {submissions.length > 0 && (
        <ol className="relative border-l border-slate-200 space-y-4 ml-3">
          {submissions.map((sub) => (
            <li key={sub.id} className="ml-4">
              <div className="absolute -left-1.5 mt-1.5 h-3 w-3 rounded-full border border-white bg-slate-400" />
              <div className="rounded-lg border border-slate-200 bg-white p-3 shadow-sm">
                <p className="text-sm font-semibold text-slate-800">{sub.nodeTitle}</p>
                <p className="text-xs text-slate-400 mb-1">{new Date(sub.submittedAt).toLocaleString()}</p>
                <p className="text-xs text-slate-600 font-mono break-all">{payloadSummary(sub.payload)}</p>
              </div>
            </li>
          ))}
        </ol>
      )}
    </div>
  )
}
