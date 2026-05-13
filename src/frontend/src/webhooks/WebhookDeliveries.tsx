import { useCallback, useEffect, useState } from 'react'

interface Webhook {
  id: string
  url: string
  events: string[]
  createdAt: string
}

interface WebhookDelivery {
  id: string
  webhookId: string
  eventType: string
  attemptCount: number
  status: 'Pending' | 'Delivered' | 'Failed'
  responseStatus: number | null
  createdAt: string
}

interface WebhookDeliveriesProps {
  apiKey?: string
}

const statusColors: Record<WebhookDelivery['status'], string> = {
  Pending: 'bg-yellow-100 text-yellow-800',
  Delivered: 'bg-green-100 text-green-800',
  Failed: 'bg-red-100 text-red-800',
}

function buildHeaders(apiKey?: string): Record<string, string> {
  const h: Record<string, string> = { 'Content-Type': 'application/json' }
  if (apiKey) h['X-Api-Key'] = apiKey
  return h
}

export function WebhookDeliveries({ apiKey }: WebhookDeliveriesProps) {
  const [webhooks, setWebhooks] = useState<Webhook[]>([])
  const [selectedWebhook, setSelectedWebhook] = useState<Webhook | null>(null)
  const [deliveries, setDeliveries] = useState<WebhookDelivery[]>([])
  const [isLoadingWebhooks, setIsLoadingWebhooks] = useState(false)
  const [isLoadingDeliveries, setIsLoadingDeliveries] = useState(false)
  const [webhookError, setWebhookError] = useState<string | null>(null)
  const [deliveryError, setDeliveryError] = useState<string | null>(null)
  const [retryingId, setRetryingId] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    setIsLoadingWebhooks(true)
    setWebhookError(null)

    fetch('/api/webhooks', { headers: buildHeaders(apiKey) })
      .then((res) => {
        if (!res.ok) throw new Error(`Failed to load webhooks (${res.status})`)
        return res.json() as Promise<Webhook[]>
      })
      .then((data) => { if (!cancelled) setWebhooks(data) })
      .catch((err: unknown) => { if (!cancelled) setWebhookError(err instanceof Error ? err.message : 'Unknown error') })
      .finally(() => { if (!cancelled) setIsLoadingWebhooks(false) })

    return () => { cancelled = true }
  }, [apiKey])

  const fetchDeliveries = useCallback((webhook: Webhook) => {
    setIsLoadingDeliveries(true)
    setDeliveryError(null)

    fetch(`/api/webhooks/${webhook.id}/deliveries`, { headers: buildHeaders(apiKey) })
      .then((res) => {
        if (!res.ok) throw new Error(`Failed to load deliveries (${res.status})`)
        return res.json() as Promise<WebhookDelivery[]>
      })
      .then((data) => setDeliveries(data))
      .catch((err: unknown) => setDeliveryError(err instanceof Error ? err.message : 'Unknown error'))
      .finally(() => setIsLoadingDeliveries(false))
  }, [apiKey])

  const retryDelivery = useCallback((deliveryId: string, webhook: Webhook) => {
    setRetryingId(deliveryId)

    fetch(`/api/webhooks/deliveries/${deliveryId}/retry`, {
      method: 'POST',
      headers: buildHeaders(apiKey),
    })
      .then((res) => {
        if (!res.ok) throw new Error(`Retry failed (${res.status})`)
        fetchDeliveries(webhook)
      })
      .catch((err: unknown) => setDeliveryError(err instanceof Error ? err.message : 'Retry failed'))
      .finally(() => setRetryingId(null))
  }, [apiKey, fetchDeliveries])

  const handleSelectWebhook = (webhook: Webhook) => {
    setSelectedWebhook(webhook)
    fetchDeliveries(webhook)
  }

  return (
    <div className="space-y-4">
      {webhookError && (
        <p className="rounded bg-rose-50 p-3 text-sm text-rose-600">{webhookError}</p>
      )}

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <div className="sm:col-span-1 space-y-2">
          <h3 className="text-sm font-semibold text-slate-700">Webhooks</h3>
          {isLoadingWebhooks && <p className="text-sm text-slate-500">Loading...</p>}
          {!isLoadingWebhooks && webhooks.length === 0 && (
            <p className="text-sm text-slate-400 italic">No webhooks configured.</p>
          )}
          {webhooks.map((wh) => (
            <button
              key={wh.id}
              onClick={() => handleSelectWebhook(wh)}
              className={`w-full rounded-lg border p-3 text-left text-sm transition-colors ${
                selectedWebhook?.id === wh.id
                  ? 'border-slate-700 bg-slate-900 text-white'
                  : 'border-slate-200 bg-white text-slate-700 hover:bg-slate-50'
              }`}
            >
              <p className="font-medium truncate">{wh.url}</p>
              <p className={`text-xs mt-1 ${selectedWebhook?.id === wh.id ? 'text-slate-300' : 'text-slate-400'}`}>
                {wh.events.join(', ')}
              </p>
            </button>
          ))}
        </div>

        <div className="sm:col-span-2 space-y-2">
          <h3 className="text-sm font-semibold text-slate-700">
            {selectedWebhook ? `Deliveries - ${selectedWebhook.url}` : 'Deliveries'}
          </h3>

          {!selectedWebhook && (
            <p className="text-sm text-slate-400 italic">Select a webhook to view deliveries.</p>
          )}

          {deliveryError && (
            <p className="rounded bg-rose-50 p-3 text-sm text-rose-600">{deliveryError}</p>
          )}

          {isLoadingDeliveries && <p className="text-sm text-slate-500">Loading...</p>}

          {selectedWebhook && !isLoadingDeliveries && deliveries.length === 0 && (
            <p className="text-sm text-slate-400 italic">No deliveries.</p>
          )}

          {deliveries.length > 0 && (
            <div className="overflow-x-auto rounded-lg border border-slate-200">
              <table className="w-full text-sm">
                <thead className="bg-slate-50 text-xs text-slate-500 uppercase">
                  <tr>
                    <th className="px-3 py-2 text-left">Event Type</th>
                    <th className="px-3 py-2 text-left">Status</th>
                    <th className="px-3 py-2 text-left">Attempts</th>
                    <th className="px-3 py-2 text-left">Response</th>
                    <th className="px-3 py-2 text-left">Created</th>
                    <th className="px-3 py-2" />
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {deliveries.map((d) => (
                    <tr key={d.id} className="bg-white">
                      <td className="px-3 py-2 font-mono text-xs">{d.eventType}</td>
                      <td className="px-3 py-2">
                        <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${statusColors[d.status]}`}>
                          {d.status}
                        </span>
                      </td>
                      <td className="px-3 py-2 text-slate-600">{d.attemptCount}</td>
                      <td className="px-3 py-2 text-slate-600">{d.responseStatus ?? '-'}</td>
                      <td className="px-3 py-2 text-slate-400 text-xs">
                        {new Date(d.createdAt).toLocaleString()}
                      </td>
                      <td className="px-3 py-2">
                        {d.status === 'Failed' && selectedWebhook && (
                          <button
                            onClick={() => retryDelivery(d.id, selectedWebhook)}
                            disabled={retryingId === d.id}
                            className="rounded border border-slate-300 px-2 py-1 text-xs text-slate-700 hover:bg-slate-50 disabled:opacity-50"
                          >
                            {retryingId === d.id ? 'Retrying...' : 'Retry'}
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
