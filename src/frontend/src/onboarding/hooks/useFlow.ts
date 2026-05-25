import { useEffect, useReducer } from 'react'
import type { FlowDefinition } from '../types/flow'

const API_KEY = import.meta.env.VITE_API_KEY as string | undefined
const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

async function fetchFlowDefinition(flowId: string): Promise<FlowDefinition> {
  const headers: Record<string, string> = {}
  if (API_KEY) headers['X-Api-Key'] = API_KEY

  const response = await fetch(`${API_BASE_URL}/api/flows/${flowId}`, { headers })
  if (!response.ok) throw new Error(`Failed to fetch flow: ${response.status}`)
  return response.json() as Promise<FlowDefinition>
}

type FlowState = {
  flow: FlowDefinition | null
  isLoading: boolean
  error: string | null
}

type FlowAction =
  | { type: 'reset'; isLoading: boolean }
  | { type: 'success'; flow: FlowDefinition }
  | { type: 'failure'; error: string }

function flowReducer(_state: FlowState, action: FlowAction): FlowState {
  switch (action.type) {
    case 'reset':
      return { flow: null, isLoading: action.isLoading, error: null }
    case 'success':
      return { flow: action.flow, isLoading: false, error: null }
    case 'failure':
      return { flow: null, isLoading: false, error: action.error }
  }
}

function initFlowState(flowId: string | null): FlowState {
  return { flow: null, isLoading: flowId !== null, error: null }
}

export function useFlow(flowId: string | null) {
  const [state, dispatch] = useReducer(flowReducer, flowId, initFlowState)

  useEffect(() => {
    if (!flowId) {
      dispatch({ type: 'reset', isLoading: false })
      return
    }

    let cancelled = false
    dispatch({ type: 'reset', isLoading: true })

    fetchFlowDefinition(flowId)
      .then((data) => {
        if (!cancelled) dispatch({ type: 'success', flow: data })
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          dispatch({
            type: 'failure',
            error: err instanceof Error ? err.message : 'Failed to load flow',
          })
        }
      })

    return () => {
      cancelled = true
    }
  }, [flowId])

  return state
}
