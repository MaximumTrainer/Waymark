import { afterEach, describe, expect, it, vi } from 'vitest'
import { resolveWorkflowApiBase, startSession } from './workflow-api-client'

describe('workflow-api-client base URL resolution', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('keeps an explicit /api/workflow base without duplicating the path', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => ({ sessionId: 'session-1', isCompleted: false, currentNode: null }),
    } as Response)

    await startSession('http://localhost:5072/api/workflow', { flowId: 'flow-1' })

    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5072/api/workflow/sessions/start',
      expect.any(Object),
    )
  })

  it('appends /api/workflow when base URL points at host root', () => {
    expect(resolveWorkflowApiBase('http://localhost:5072')).toBe('http://localhost:5072/api/workflow')
    expect(resolveWorkflowApiBase('')).toBe('/api/workflow')
  })
})
