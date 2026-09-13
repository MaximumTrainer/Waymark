import { useCallback, useEffect, useRef, useState } from 'react'
import type { SessionStepResponse, StartSessionRequest, SubmitStepRequest } from '../types/flow'
import {
  startSession as apiStartSession,
  submitStep as apiSubmitStep,
  getNextStep as apiGetNextStep,
  resolveWorkflowApiBase,
  ComplianceError,
  ExpiredSessionError,
} from '../api/workflow-api-client'
import { createSessionEventSource } from './session-event-source'
import { clearApplicantToken, withApplicantTokenQuery } from '../api/applicant-session'

const serverBase = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')
const workflowApiBase = resolveWorkflowApiBase(serverBase)

export function useOnboarding() {
  const [step, setStep] = useState<SessionStepResponse | null>(null)
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [isCompleted, setIsCompleted] = useState(false)
  // Tracked apart from `error`: an expired credential is a dead end the applicant cannot retry
  // past, and saying so is the difference between "start again" and an unexplained failure.
  const [isExpired, setIsExpired] = useState(false)
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
      const next = await apiGetNextStep(serverBase, sessionId)
      setStep(next)
      return next
    } catch (requestError) {
      if (requestError instanceof ExpiredSessionError) setIsExpired(true)
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
        // EventSource cannot set headers, so the credential rides in the query string.
        withApplicantTokenQuery(`${workflowApiBase}/sessions/${sessionId}/events`),
        {
          onStepAdvanced: (data) => setStep(data),
          onCompleted: () => {
            setIsCompleted(true)
            setStep((prev) => prev ? { ...prev, isCompleted: true, currentNode: null } : null)
            eventSourceRef.current = null
            // The journey is over, so the credential has nothing left to authorise. Dropping it
            // keeps a finished applicant's token off a shared machine rather than leaving it in
            // sessionStorage until the tab closes.
            clearApplicantToken()
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
      const next = await apiStartSession(serverBase, payload)
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
      const next = await apiSubmitStep(serverBase, sessionId, nodeId, payload)
      setStep(next)
      if (next.isCompleted) clearApplicantToken()
      return next
    } catch (requestError) {
      if (requestError instanceof ExpiredSessionError) setIsExpired(true)
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
    isExpired,
    isCompleted,
    startSession,
    submitStep,
    getNextStep,
  }
}

