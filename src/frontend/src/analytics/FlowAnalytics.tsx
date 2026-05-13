import { useEffect, useState } from 'react'

interface FlowAnalyticsData {
  flowId: string
  flowName: string
  totalSessions: number
  completedSessions: number
  abandonedSessions: number
  averageDurationSeconds: number
  completionRate: number
  topAbandonmentNodeTitle?: string | null
}

interface FlowAnalyticsProps {
  flowId: string
  apiKey?: string
}

function formatDuration(seconds: number): string {
  const m = Math.floor(seconds / 60)
  const s = Math.floor(seconds % 60)
  return `${m}m ${s}s`
}

function MetricCard({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
      <p className="text-xs font-medium text-slate-500 uppercase tracking-wide">{label}</p>
      <p className="mt-1 text-2xl font-bold text-slate-900">{String(value)}</p>
    </div>
  )
}

function SkeletonCard() {
  return (
    <div className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm animate-pulse">
      <div className="h-3 w-24 rounded bg-slate-200 mb-2" />
      <div className="h-7 w-16 rounded bg-slate-200" />
    </div>
  )
}

export function FlowAnalytics({ flowId, apiKey }: FlowAnalyticsProps) {
  const [data, setData] = useState<FlowAnalyticsData | null>(null)
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [refreshKey, setRefreshKey] = useState(0)

  useEffect(() => {
    let cancelled = false

    const load = async () => {
      setIsLoading(true)
      setError(null)
      const headers: Record<string, string> = {}
      if (apiKey) headers['X-Api-Key'] = apiKey
      try {
        const res = await fetch(`/api/analytics/flows/${flowId}`, { headers })
        if (!res.ok) throw new Error(`Failed to load analytics (${res.status})`)
        if (!cancelled) setData((await res.json()) as FlowAnalyticsData)
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Unknown error')
      } finally {
        if (!cancelled) setIsLoading(false)
      }
    }

    void load()
    return () => { cancelled = true }
  }, [flowId, apiKey, refreshKey])

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h3 className="text-base font-semibold text-slate-800">Flow Analytics</h3>
        <button
          onClick={() => setRefreshKey((k) => k + 1)}
          disabled={isLoading}
          className="rounded border border-slate-300 px-3 py-1 text-xs text-slate-700 hover:bg-slate-50 disabled:opacity-50"
        >
          {isLoading ? 'Refreshing…' : 'Refresh'}
        </button>
      </div>

      {error && (
        <p className="rounded bg-rose-50 p-3 text-sm text-rose-600">{error}</p>
      )}

      {isLoading && !data && (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => <SkeletonCard key={i} />)}
        </div>
      )}

      {data && (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
          <MetricCard label="Total Sessions" value={data.totalSessions} />
          <MetricCard label="Completed Sessions" value={data.completedSessions} />
          <MetricCard label="Abandoned Sessions" value={data.abandonedSessions} />
          <MetricCard label="Completion Rate" value={`${(data.completionRate * 100).toFixed(1)}%`} />
          <MetricCard label="Avg Duration" value={formatDuration(data.averageDurationSeconds)} />
          {data.topAbandonmentNodeTitle ? (
            <MetricCard label="Top Abandonment Node" value={data.topAbandonmentNodeTitle} />
          ) : (
            <div className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
              <p className="text-xs font-medium text-slate-500 uppercase tracking-wide">Top Abandonment Node</p>
              <p className="mt-1 text-sm text-slate-400 italic">None</p>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
