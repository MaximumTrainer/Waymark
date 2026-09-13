/**
 * Formatting for the session event trail, kept apart from the component so both can be tested and
 * so the component file exports only a component.
 */

/**
 * Renders the gap since the previous event. This is the column that makes a stall visible: a
 * fifteen-minute pause on one step reads very differently from fifteen seconds.
 */
export function formatGap(previousIso: string | null, currentIso: string): string {
  if (previousIso === null) return '—'

  const gapMs = new Date(currentIso).getTime() - new Date(previousIso).getTime()
  if (!Number.isFinite(gapMs)) return '—'

  // Clock skew between a browser and the server can put a client event fractionally before the
  // server event it followed. Showing a negative gap would read as an ordering bug.
  if (gapMs < 0) return '—'
  if (gapMs < 1000) return `+${gapMs}ms`

  const seconds = gapMs / 1000
  if (seconds < 60) return `+${seconds.toFixed(1)}s`

  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `+${minutes}m ${Math.round(seconds % 60)}s`

  const hours = Math.floor(minutes / 60)
  return `+${hours}h ${minutes % 60}m`
}

export function payloadSummary(payload: Record<string, unknown> | null | undefined): string | null {
  if (!payload) return null

  const entries = Object.entries(payload)
  if (entries.length === 0) return null

  return entries
    .slice(0, 4)
    .map(([key, value]) => `${key}: ${String(value)}`)
    .join(', ') + (entries.length > 4 ? ', …' : '')
}

export function isClient(source: string): boolean {
  return source.toLowerCase() === 'client'
}
