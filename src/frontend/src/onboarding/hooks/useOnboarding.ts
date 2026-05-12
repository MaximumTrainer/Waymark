import { useCallback, useEffect, useRef, useState } from 'react'
import type { SessionStepResponse, StartSessionRequest, SubmitStepRequest } from '../types/flow'

const apiBase = (import.meta.env.VITE_API_BASE_URL ?? '/api/workflow').replace(/\/$/, '')

export function useOnboarding() {
  const [step, setStep] = useState<SessionStepResponse | null>(null)
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [isCompleted, setIsCompleted] = useState(false)
  const eventSourceRef = useRef<EventSource | null>(null)
  const pollingRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const sessionIdRef = useRef<string | null>(null)

  const closeEventSource = useCallback(() => {
    eventSourceRef.current?.close()
    eventSourceRef.current = null
    if (pollingRef.current !== null) {
      clearInterval(pollingRef.current)
      pollingRef.current = null
    }
  }, [])

  useEffect(() => {
    return () => {
      closeEventSource()
    }
  }, [closeEventSource])

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

  const openEventStream = useCallback((sessionId: string) => {
    closeEventSource()
    sessionIdRef.current = sessionId

    if (typeof EventSource !== 'undefined') {
      const evtSource = new EventSource(`${apiBase}/sessions/${sessionId}/events`)
      eventSourceRef.current = evtSource

      evtSource.addEventListener('step-advanced', (e) => {
        const data = JSON.parse((e as MessageEvent).data) as SessionStepResponse
        setStep(data)
      })

      evtSource.addEventListener('session-completed', () => {
        setIsCompleted(true)
        setStep(null)
        evtSource.close()
        eventSourceRef.current = null
      })

      evtSource.addEventListener('session-abandoned', () => {
        evtSource.close()
        eventSourceRef.current = null
      })
    } else {
      // Fallback: poll every 5s
      pollingRef.current = setInterval(() => {
        void getNextStep(sessionId)
      }, 5000)
    }
  }, [closeEventSource]) // eslint-disable-line react-hooks/exhaustive-deps

  const startSession = useCallback(async (payload: StartSessionRequest) => {
    const next = await request<SessionStepResponse>('/sessions/start', {
      method: 'POST',
      body: JSON.stringify(payload),
    })

    setStep(next)
    setIsCompleted(false)
    openEventStream(next.sessionId)
    return next
  }, [request, openEventStream])

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
    isCompleted,
    startSession,
    submitStep,
    getNextStep,
  }
}
