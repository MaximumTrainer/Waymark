import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createSessionEventSource } from './session-event-source'
import type { SessionStepResponse } from '../types/flow'

class MockEventSource {
  static instances: MockEventSource[] = []
  url: string
  onerror: ((e: Event) => void) | null = null
  closed = false
  private listeners: Record<string, ((e: Event) => void)[]> = {}

  constructor(url: string) {
    this.url = url
    MockEventSource.instances.push(this)
  }

  addEventListener(type: string, handler: (e: Event) => void) {
    this.listeners[type] ??= []
    this.listeners[type].push(handler)
  }

  dispatchEvent(type: string, data?: unknown) {
    const e = { data: JSON.stringify(data) } as MessageEvent
    for (const h of this.listeners[type] ?? []) h(e)
  }

  fireError() {
    this.onerror?.(new Event('error'))
  }

  close() {
    this.closed = true
  }
}

beforeEach(() => {
  MockEventSource.instances = []
  vi.stubGlobal('EventSource', MockEventSource)
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('createSessionEventSource', () => {
  it('fires onStepAdvanced when step-advanced event received', () => {
    const onStepAdvanced = vi.fn()
    createSessionEventSource('http://localhost/events', {
      onStepAdvanced,
      onCompleted: vi.fn(),
      onAbandoned: vi.fn(),
      onError: vi.fn(),
    })

    const [es] = MockEventSource.instances
    const stepData: SessionStepResponse = {
      sessionId: 'session-1',
      isCompleted: false,
      currentNode: { id: 'node-1', key: 'step1', type: 'Form', title: 'Step 1', jsonContent: '{}' },
    }
    es.dispatchEvent('step-advanced', stepData)

    expect(onStepAdvanced).toHaveBeenCalledWith(stepData)
  })

  it('fires onCompleted and closes EventSource when session-completed event received', () => {
    const onCompleted = vi.fn()
    createSessionEventSource('http://localhost/events', {
      onStepAdvanced: vi.fn(),
      onCompleted,
      onAbandoned: vi.fn(),
      onError: vi.fn(),
    })

    const [es] = MockEventSource.instances
    es.dispatchEvent('session-completed')

    expect(onCompleted).toHaveBeenCalledOnce()
    expect(es.closed).toBe(true)
  })

  it('calls onError and closes EventSource when onerror fires', () => {
    const onError = vi.fn()
    createSessionEventSource('http://localhost/events', {
      onStepAdvanced: vi.fn(),
      onCompleted: vi.fn(),
      onAbandoned: vi.fn(),
      onError,
    })

    const [es] = MockEventSource.instances
    es.fireError()

    expect(onError).toHaveBeenCalledOnce()
    expect(es.closed).toBe(true)
  })

  it('close() function closes the EventSource', () => {
    const { close } = createSessionEventSource('http://localhost/events', {
      onStepAdvanced: vi.fn(),
      onCompleted: vi.fn(),
      onAbandoned: vi.fn(),
      onError: vi.fn(),
    })

    const [es] = MockEventSource.instances
    close()

    expect(es.closed).toBe(true)
  })
})
