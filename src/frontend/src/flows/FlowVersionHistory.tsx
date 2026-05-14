import { useCallback, useEffect, useMemo, useState } from 'react'

interface VersionSummary {
  versionNumber: number
  createdAt: string
  createdBy?: string | null
}

interface FlowVersionHistoryProps {
  flowId: string
  apiKey?: string
  onRestore?: (versionNumber: number) => void
  activePersonasByVersion?: Record<number, string[]>
}

const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

export function FlowVersionHistory({ flowId, apiKey, onRestore, activePersonasByVersion = {} }: FlowVersionHistoryProps) {
  const [versions, setVersions] = useState<VersionSummary[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [restoring, setRestoring] = useState<number | null>(null)
  const [confirmVersion, setConfirmVersion] = useState<number | null>(null)

  const headers = useMemo(
    (): Record<string, string> => (apiKey ? { 'X-Api-Key': apiKey } : {}),
    [apiKey],
  )

  useEffect(() => {
    let cancelled = false
    fetch(`${API_BASE_URL}/api/flows/${flowId}/versions`, { headers })
      .then(r => {
        if (!r.ok) throw new Error(`HTTP ${r.status}`)
        return r.json() as Promise<VersionSummary[]>
      })
      .then(data => { if (!cancelled) setVersions(data) })
      .catch(e => { if (!cancelled) setError((e as Error).message) })
    return () => { cancelled = true }
  }, [flowId, headers])

  const handleRestore = useCallback(async (versionNumber: number) => {
    setRestoring(versionNumber)
      setConfirmVersion(null)
    try {
      const r = await fetch(`${API_BASE_URL}/api/flows/${flowId}/versions/${versionNumber}/restore`, {
        method: 'POST',
        headers,
      })
      if (!r.ok) throw new Error(`HTTP ${r.status}`)
      onRestore?.(versionNumber)
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setRestoring(null)
    }
  }, [flowId, headers, onRestore])

  if (versions === null && error === null) return <div className="p-4 text-slate-500">Loading version history…</div>
  if (error) return <div className="p-4 text-red-600">Error: {error}</div>
  if (!versions || versions.length === 0) return <div className="p-4 text-slate-400">No version history yet.</div>

  return (
    <div className="rounded-lg border border-slate-200 bg-white">
      <h3 className="px-4 py-3 text-sm font-semibold text-slate-700 border-b border-slate-200">Version History</h3>
      <ul className="divide-y divide-slate-100">
        {versions.map(v => (
          <li key={v.versionNumber} className="flex items-center justify-between px-4 py-3">
            <div>
              <span className="text-sm font-medium text-slate-900">Version {v.versionNumber}</span>
              <span className="ml-3 text-xs text-slate-500">{new Date(v.createdAt).toLocaleString()}</span>
              {v.createdBy && <span className="ml-2 text-xs text-slate-400">by {v.createdBy}</span>}
              {(activePersonasByVersion[v.versionNumber]?.length ?? 0) > 0 && (
                <span className="ml-2 rounded bg-indigo-50 px-2 py-0.5 text-xs text-indigo-700">
                  Live for: {activePersonasByVersion[v.versionNumber].join(', ')}
                </span>
              )}
            </div>
            <button
              onClick={() => setConfirmVersion(v.versionNumber)}
              disabled={restoring !== null}
              className="text-xs px-3 py-1 rounded border border-indigo-300 text-indigo-700 hover:bg-indigo-50 disabled:opacity-50"
            >
              {restoring === v.versionNumber ? 'Restoring…' : 'Restore'}
            </button>
          </li>
        ))}
      </ul>
      {confirmVersion !== null && (
        <div className="fixed inset-0 bg-black/40 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 max-w-sm mx-4 shadow-xl">
            <h4 className="text-base font-semibold mb-2">Restore Version {confirmVersion}?</h4>
            <p className="text-sm text-slate-600 mb-4">This will replace the current flow with the saved version {confirmVersion} snapshot. This action can be undone by restoring a newer version.</p>
            <div className="flex gap-3 justify-end">
              <button onClick={() => setConfirmVersion(null)} className="px-4 py-2 text-sm rounded border border-slate-300 hover:bg-slate-50">Cancel</button>
              <button onClick={() => handleRestore(confirmVersion)} className="px-4 py-2 text-sm rounded bg-indigo-600 text-white hover:bg-indigo-700">Restore</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
