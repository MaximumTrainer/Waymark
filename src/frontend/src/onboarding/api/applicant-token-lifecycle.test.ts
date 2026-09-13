import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ExpiredSessionError, getNextStep, startSession, submitStep } from './workflow-api-client'
import { clearApplicantToken, getApplicantToken } from './applicant-session'

/**
 * The applicant credential is issued once at session start and renewed on each step submission. Two
 * behaviours matter to the browser: it has to store the renewal, and it has to tell an expired
 * credential apart from a server fault — one is a dead end the applicant must be told about, the
 * other is a failure they can retry.
 */
describe('applicant token lifecycle', () => {
  beforeEach(() => {
    clearApplicantToken()
  })

  afterEach(() => {
    vi.restoreAllMocks()
    clearApplicantToken()
  })

  const jsonResponse = (status: number, body: unknown) => ({
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  } as Response)

  describe('renewal', () => {
    it('stores the token returned by session start', async () => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(200, {
        sessionId: 'session-1',
        isCompleted: false,
        currentNode: null,
        applicantToken: 'token-from-start',
      }))

      await startSession('', { flowId: 'flow-1' })

      expect(getApplicantToken()).toBe('token-from-start')
    })

    it('replaces the stored token with the one a step submission returns', async () => {
      vi.spyOn(globalThis, 'fetch')
        .mockResolvedValueOnce(jsonResponse(200, {
          sessionId: 'session-1',
          isCompleted: false,
          currentNode: null,
          applicantToken: 'token-from-start',
        }))
        .mockResolvedValueOnce(jsonResponse(200, {
          sessionId: 'session-1',
          isCompleted: false,
          currentNode: null,
          applicantToken: 'refreshed-token',
        }))

      await startSession('', { flowId: 'flow-1' })
      await submitStep('', 'session-1', 'node-1', { payload: {} })

      // This is what keeps a long journey alive: without it the browser keeps presenting the
      // original credential until it expires mid-application.
      expect(getApplicantToken()).toBe('refreshed-token')
    })

    it('sends the refreshed token on the next request', async () => {
      const fetchSpy = vi.spyOn(globalThis, 'fetch')
        .mockResolvedValueOnce(jsonResponse(200, {
          sessionId: 'session-1',
          isCompleted: false,
          currentNode: null,
          applicantToken: 'token-from-start',
        }))
        .mockResolvedValueOnce(jsonResponse(200, {
          sessionId: 'session-1',
          isCompleted: false,
          currentNode: null,
          applicantToken: 'refreshed-token',
        }))
        .mockResolvedValueOnce(jsonResponse(200, {
          sessionId: 'session-1',
          isCompleted: false,
          currentNode: null,
        }))

      await startSession('', { flowId: 'flow-1' })
      await submitStep('', 'session-1', 'node-1', { payload: {} })
      await getNextStep('', 'session-1')

      const lastCall = fetchSpy.mock.calls.at(-1)!
      const headers = (lastCall[1] as RequestInit).headers as Record<string, string>
      expect(headers.Authorization).toBe('Bearer refreshed-token')
    })

    it('keeps the stored token when a response carries no renewal', async () => {
      // An operator-submitted step returns none; the browser must not wipe what it holds.
      vi.spyOn(globalThis, 'fetch')
        .mockResolvedValueOnce(jsonResponse(200, {
          sessionId: 'session-1',
          isCompleted: false,
          currentNode: null,
          applicantToken: 'token-from-start',
        }))
        .mockResolvedValueOnce(jsonResponse(200, {
          sessionId: 'session-1',
          isCompleted: false,
          currentNode: null,
        }))

      await startSession('', { flowId: 'flow-1' })
      await submitStep('', 'session-1', 'node-1', { payload: {} })

      expect(getApplicantToken()).toBe('token-from-start')
    })
  })

  describe('expired credentials', () => {
    it('raises ExpiredSessionError when a step submission is refused with 401', async () => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(401, {}))

      await expect(submitStep('', 'session-1', 'node-1', { payload: {} }))
        .rejects.toBeInstanceOf(ExpiredSessionError)
    })

    it('raises ExpiredSessionError when the session has reached a terminal status', async () => {
      // 403 on a session this browser was completing means its token is no longer accepted for
      // writes. From the applicant side that is the same dead end as an expiry.
      vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(403, {}))

      await expect(submitStep('', 'session-1', 'node-1', { payload: {} }))
        .rejects.toBeInstanceOf(ExpiredSessionError)
    })

    it('raises ExpiredSessionError when reading the next step is refused with 401', async () => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(401, {}))

      await expect(getNextStep('', 'session-1')).rejects.toBeInstanceOf(ExpiredSessionError)
    })

    it('carries a message the applicant can act on', async () => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(401, {}))

      const error = await submitStep('', 'session-1', 'node-1', { payload: {} }).catch((e) => e)

      expect(error.message).toMatch(/expired/i)
    })

    it('leaves a server fault as a generic error, not an expiry', async () => {
      // A 500 is a failure the applicant can retry past; telling them to start again would throw
      // away a journey that is still perfectly valid.
      vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(500, {}))

      const error = await submitStep('', 'session-1', 'node-1', { payload: {} }).catch((e) => e)

      expect(error).not.toBeInstanceOf(ExpiredSessionError)
      expect(error).toBeInstanceOf(Error)
    })
  })
})
