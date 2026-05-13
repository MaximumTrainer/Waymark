import type { SessionStepResponse } from '../types/flow'

export interface SessionEventSourceCallbacks {
  onStepAdvanced: (data: SessionStepResponse) => void
  onCompleted: () => void
  onAbandoned: () => void
  onError: (err: Event) => void
}

export function createSessionEventSource(
  url: string,
  callbacks: SessionEventSourceCallbacks,
): { close: () => void } {
  const es = new EventSource(url)

  es.addEventListener('step-advanced', (e) => {
    callbacks.onStepAdvanced(JSON.parse((e as MessageEvent).data) as SessionStepResponse)
  })

  es.addEventListener('session-completed', () => {
    callbacks.onCompleted()
    es.close()
  })

  es.addEventListener('session-abandoned', () => {
    callbacks.onAbandoned()
    es.close()
  })

  es.onerror = (e) => {
    es.close()
    callbacks.onError(e)
  }

  return { close: () => es.close() }
}
