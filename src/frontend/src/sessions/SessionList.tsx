import { useEffect, useState } from 'react'

export interface SessionSummary {
  id: string
  flowId: string
  flowName?: string
  status: 'InProgress' | 'Completed' | 'Abandoned'
  createdAt: string
  updatedAt: string
}

interface SessionListProps {
  apiKey?: string
  onSelectSession: (session: SessionSummary) => void
}

const statusBadge: Record<SessionSummary['status'], string> = {
  InProgress: 'bg-yellow-100 text-yellow-800',
  Completed: 'bg-green-100 text-green-800',
  Abandoned: 'bg-red-100 text-red-800',
}

function buildHeaders(apiKey?: string): Record<string, string> {
  const h: Record<string, string> = {}
  if (apiKey) h['X-Api-Key'] = apiKey
  return h
}

export function SessionList({ apiKey, onSelectSession }: SessionListProps) {
  const [sessions, setSessions] = useState<SessionSummary[]>([])
  const [page, setPage] = useState(1)
  const [hasMore, setHasMore] = useState(false)
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const pageSize = 20

  useEffect(() => {
    let cancelled = false
    setIsLoading(true)
    setError(null)

    fetch(
      `/api/workflow/sessions?page=${page}&pageSize=${pageSize}`,
      { headers: buildHeaders(apiKey) },
    )
      .then((res) => {
        if (!res.ok) throw new Error(`Failed to load sessions (${res.status})`)
        return res.json() as Promise<SessionSummary[]>
      })
      .then((data) => {
        if (!cancelled) {
          setSessions(data)
          setHasMore(data.length === pageSize)
        }
      })
      .catch((err: unknown) => { if (!cancelled) setError(err instanceof Error ? err.message : 'Unknown error') })
      .finally(() => { if (!cancelled) setIsLoading(false) })

    return () => { cancelled = true }
  }, [apiKey, page])

  return (
    <div className="space-y-4">
      {error && <p className="rounded bg-rose-50 p-3 text-sm text-rose-600">{error}</p>}

      {isLoading && <p className="text-sm text-slate-500">Loading sessions…</p>}

      {!isLoading && sessions.length === 0 && (
        <p className="text-sm text-slate-400 italic">No sessions found.</p>
      )}

      {sessions.length > 0 && (
        <div className="overflow-x-auto rounded-lg border border-slate-200">
          <table className="w-full text-sm">
            <thead className="bg-slate-50 text-xs text-slate-500 uppercase">
              <tr>
                <th className="px-3 py-2 text-left">Flow</th>
                <th className="px-3 py-2 text-left">Status</th>
                <th className="px-3 py-2 text-left">Created</th>
                <th className="px-3 py-2 text-left">Updated</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {sessions.map((s) => (
                <tr
                  key={s.id}
                  onClick={() => onSelectSession(s)}
                  className="bg-white cursor-pointer hover:bg-slate-50"
                >
                  <td className="px-3 py-2 text-slate-800">{s.flowName ?? s.flowId}</td>
                  <td className="px-3 py-2">
                    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${statusBadge[s.status]}`}>
                      {s.status}
                    </span>
                  </td>
                  <td className="px-3 py-2 text-slate-400 text-xs">{new Date(s.createdAt).toLocaleString()}</td>
                  <td className="px-3 py-2 text-slate-400 text-xs">{new Date(s.updatedAt).toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="flex items-center gap-2">
        <button
          onClick={() => setPage((p) => Math.max(1, p - 1))}
          disabled={page === 1 || isLoading}
          className="rounded border border-slate-300 px-3 py-1 text-xs text-slate-700 hover:bg-slate-50 disabled:opacity-50"
        >
          Prev page
        </button>
        <span className="text-xs text-slate-500">Page {page}</span>
        <button
          onClick={() => setPage((p) => p + 1)}
          disabled={!hasMore || isLoading}
          className="rounded border border-slate-300 px-3 py-1 text-xs text-slate-700 hover:bg-slate-50 disabled:opacity-50"
        >
          Next page
        </button>
      </div>
    </div>
  )
}
