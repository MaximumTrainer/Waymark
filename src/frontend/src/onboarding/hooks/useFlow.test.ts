import { renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { useFlow } from './useFlow'

beforeEach(() => {
  vi.clearAllMocks()
})

afterEach(() => {
  vi.restoreAllMocks()
})

const mockFlow = {
  id: 'flow-1',
  name: 'Test Flow',
  nodes: [],
  edges: [],
}

describe('useFlow', () => {
  it('returns null flow and not loading when flowId is null', () => {
    const { result } = renderHook(() => useFlow(null))
    expect(result.current.flow).toBeNull()
    expect(result.current.isLoading).toBe(false)
    expect(result.current.error).toBeNull()
  })

  it('fetches and returns flow data when flowId is provided', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValueOnce({
        ok: true,
        json: async () => mockFlow,
      }),
    )

    const { result } = renderHook(() => useFlow('flow-1'))

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.flow).toEqual(mockFlow)
    })

    expect(result.current.isLoading).toBe(false)
    expect(result.current.error).toBeNull()
  })

  it('sets error when fetch fails', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValueOnce({
        ok: false,
        status: 404,
      }),
    )

    const { result } = renderHook(() => useFlow('flow-1'))

    await waitFor(() => {
      expect(result.current.error).toBe('Failed to fetch flow: 404')
    })

    expect(result.current.flow).toBeNull()
    expect(result.current.isLoading).toBe(false)
  })
})
