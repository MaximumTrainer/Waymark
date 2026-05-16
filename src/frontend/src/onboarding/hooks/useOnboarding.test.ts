import { renderHook, act } from '@testing-library/react'
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { useOnboarding } from './useOnboarding'

vi.mock('../api/workflow-api-client', () => ({
  startSession: vi.fn(),
  submitStep: vi.fn(),
  getNextStep: vi.fn(),
  resolveWorkflowApiBase: (base: string) => `${base}/api/workflow`,
  ComplianceError: class ComplianceError extends Error {
    violations: Array<{ field: string; message: string; ruleId: string }>
    constructor(violations: Array<{ field: string; message: string; ruleId: string }>) {
      super('Compliance violation')
      this.violations = violations
    }
  },
}))

vi.mock('./session-event-source', () => ({
  createSessionEventSource: vi.fn(() => ({ close: vi.fn() })),
}))

import * as workflowApi from '../api/workflow-api-client'

const mockApiStartSession = vi.mocked(workflowApi.startSession)
const mockApiSubmitStep = vi.mocked(workflowApi.submitStep)

import type { NodeType } from '../types/flow'

const makeStep = () => ({
  sessionId: 'session-abc',
  isCompleted: false,
  currentNode: { id: 'node-1', key: 'step-1', type: 'Form' as NodeType, title: 'Step 1', jsonContent: '{}' },
})

beforeEach(() => {
  vi.clearAllMocks()
  vi.stubGlobal('EventSource', undefined)
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('useOnboarding', () => {
  it('returns initial state with no step and not loading', () => {
    const { result } = renderHook(() => useOnboarding())
    expect(result.current.step).toBeNull()
    expect(result.current.isLoading).toBe(false)
    expect(result.current.error).toBeNull()
    expect(result.current.isCompleted).toBe(false)
  })

  it('sets step after startSession resolves', async () => {
    const step = makeStep()
    mockApiStartSession.mockResolvedValueOnce(step)

    const { result } = renderHook(() => useOnboarding())

    await act(async () => {
      await result.current.startSession({ flowId: 'flow-1' })
    })

    expect(result.current.step).toEqual(step)
    expect(result.current.isLoading).toBe(false)
    expect(result.current.error).toBeNull()
  })

  it('sets error when startSession fails', async () => {
    mockApiStartSession.mockRejectedValueOnce(new Error('Network error'))

    const { result } = renderHook(() => useOnboarding())

    await act(async () => {
      try {
        await result.current.startSession({ flowId: 'flow-1' })
      } catch {
        // expected
      }
    })

    expect(result.current.error).toBe('Network error')
    expect(result.current.step).toBeNull()
  })

  it('sets step after submitStep resolves', async () => {
    const nextStep = makeStep()
    mockApiSubmitStep.mockResolvedValueOnce(nextStep)

    const { result } = renderHook(() => useOnboarding())

    await act(async () => {
      await result.current.submitStep('session-abc', 'node-1', { payload: {} })
    })

    expect(result.current.step).toEqual(nextStep)
  })

  it('does not set global error when submitStep throws a ComplianceError', async () => {
    const { ComplianceError } = await import('../api/workflow-api-client')
    const complianceError = new ComplianceError([{ field: 'email', message: 'Invalid', ruleId: 'r1' }])
    mockApiSubmitStep.mockRejectedValueOnce(complianceError)

    const { result } = renderHook(() => useOnboarding())

    await act(async () => {
      try {
        await result.current.submitStep('session-abc', 'node-1', { payload: {} })
      } catch {
        // expected
      }
    })

    expect(result.current.error).toBeNull()
  })
})
