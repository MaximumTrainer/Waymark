import { useCallback, useState } from 'react'
import type { SessionStepResponse, StartSessionRequest, SubmitStepRequest } from '../types/flow'

const apiBase = (import.meta.env.VITE_API_BASE_URL ?? '/api/workflow').replace(/\/$/, '')

export function useOnboarding() {
  const [step, setStep] = useState<SessionStepResponse | null>(null)
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const request = useCallback(async <TResponse,>(path: string, options: RequestInit): Promise<TResponse> => {
    setIsLoading(true)
    setError(null)

    try {
      const response = await fetch(`${apiBase}${path}`, {
        ...options,
        headers: {
          'Content-Type': 'application/json',
          ...(options.headers ?? {}),
        },
      })

      if (!response.ok) {
        throw new Error(`Onboarding API request failed with status ${response.status}`)
      }

      return await response.json() as TResponse
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Unknown onboarding error')
      throw requestError
    } finally {
      setIsLoading(false)
    }
  }, [])

  const startSession = useCallback(async (payload: StartSessionRequest) => {
    const next = await request<SessionStepResponse>('/sessions/start', {
      method: 'POST',
      body: JSON.stringify(payload),
    })

    setStep(next)
    return next
  }, [request])

  const submitStep = useCallback(async (sessionId: string, nodeId: string, payload: SubmitStepRequest) => {
    const next = await request<SessionStepResponse>(`/sessions/${sessionId}/steps/${nodeId}/submit`, {
      method: 'POST',
      body: JSON.stringify(payload),
    })

    setStep(next)
    return next
  }, [request])

  const getNextStep = useCallback(async (sessionId: string) => {
    const next = await request<SessionStepResponse>(`/sessions/${sessionId}/next`, {
      method: 'GET',
    })

    setStep(next)
    return next
  }, [request])

  return {
    step,
    isLoading,
    error,
    startSession,
    submitStep,
    getNextStep,
  }
}
