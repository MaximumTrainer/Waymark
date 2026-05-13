import { useEffect, useState } from 'react'
import type { FlowDefinition } from '../types/flow'

const API_KEY = import.meta.env.VITE_API_KEY as string | undefined

async function fetchFlowDefinition(flowId: string): Promise<FlowDefinition> {
  const headers: Record<string, string> = {}
  if (API_KEY) headers['X-Api-Key'] = API_KEY

  const response = await fetch(`/api/flows/${flowId}`, { headers })
  if (!response.ok) throw new Error(`Failed to fetch flow: ${response.status}`)
  return response.json() as Promise<FlowDefinition>
}

export function useFlow(flowId: string | null) {
  const [flow, setFlow] = useState<FlowDefinition | null>(null)
  const [isLoading, setIsLoading] = useState(flowId !== null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!flowId) return
    const controller = new AbortController()
    fetchFlowDefinition(flowId)
      .then((data) => {
        if (!controller.signal.aborted) {
          setFlow(data)
          setIsLoading(false)
        }
      })
      .catch((err: unknown) => {
        if (!controller.signal.aborted) {
          setError(err instanceof Error ? err.message : 'Failed to load flow')
          setIsLoading(false)
        }
      })
    return () => controller.abort()
  }, [flowId])

  return { flow, isLoading, error }
}

