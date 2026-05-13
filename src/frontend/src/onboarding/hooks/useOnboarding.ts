import { useCallback, useEffect, useRef, useState } from 'react'
import type { SessionStepResponse, StartSessionRequest, SubmitStepRequest } from '../types/flow'
import {
  startSession as apiStartSession,
  submitStep as apiSubmitStep,
  getNextStep as apiGetNextStep,
  resolveWorkflowApiBase,
  ComplianceError,
} from '../api/workflow-api-client'
import { createSessionEventSource } from './session-event-source'

const serverBase = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')
const workflowApiBase = resolveWorkflowApiBase(serverBase)
const apiKey = import.meta.env.VITE_API_KEY || undefined

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

  // Declared before openEventStream so the polling fallback can reference it
  const getNextStep = useCallback(async (sessionId: string) => {
    setIsLoading(true)
    setError(null)
    try {
      const next = await apiGetNextStep(serverBase, sessionId, apiKey)
      setStep(next)
      return next
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
      const handle = createSessionEventSource(
        `${workflowApiBase}/sessions/${sessionId}/events`,
        {
          onStepAdvanced: (data) => setStep(data),
          onCompleted: () => {
            setIsCompleted(true)
            setStep(null)
            eventSourceRef.current = null
          },
          onAbandoned: () => {
            eventSourceRef.current = null
          },
          onError: () => {
            eventSourceRef.current = null
            setError('Connection to session stream lost. Please refresh.')
          },
        },
      )
      // Store a compatible ref so closeEventSource can still close it
      eventSourceRef.current = { close: handle.close } as unknown as EventSource
    } else {
      // Fallback: poll every 5s
      pollingRef.current = setInterval(() => {
        void getNextStep(sessionId)
      }, 5000)
    }
  }, [closeEventSource, getNextStep])

  const startSession = useCallback(async (payload: StartSessionRequest) => {
    setIsLoading(true)
    setError(null)
    try {
      const next = await apiStartSession(serverBase, payload, apiKey)
      setStep(next)
      setIsCompleted(false)
      openEventStream(next.sessionId)
      return next
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Unknown onboarding error')
      throw requestError
    } finally {
      setIsLoading(false)
    }
  }, [openEventStream])

  const submitStep = useCallback(async (sessionId: string, nodeId: string, payload: SubmitStepRequest) => {
    setIsLoading(true)
    setError(null)
    try {
      const next = await apiSubmitStep(serverBase, sessionId, nodeId, payload, apiKey)
      setStep(next)
      return next
    } catch (requestError) {
      // ComplianceErrors are handled by the form component — don't set global error
      if (!(requestError instanceof ComplianceError)) {
        setError(requestError instanceof Error ? requestError.message : 'Unknown onboarding error')
      }
      throw requestError
    } finally {
      setIsLoading(false)
    }
  }, [])

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

