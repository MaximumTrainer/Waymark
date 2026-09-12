import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  applicantAuthHeaders,
  clearApplicantToken,
  getApplicantToken,
  setApplicantToken,
  withApplicantTokenQuery,
} from './applicant-session'

describe('applicant session credential', () => {
  beforeEach(() => {
    clearApplicantToken()
    sessionStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
    clearApplicantToken()
  })

  it('has no credential before a session starts', () => {
    expect(getApplicantToken()).toBeNull()
    expect(applicantAuthHeaders()).toEqual({})
  })

  it('returns a bearer header once a session has started', () => {
    setApplicantToken('token-abc')

    expect(applicantAuthHeaders()).toEqual({ Authorization: 'Bearer token-abc' })
  })

  it('never emits an X-Api-Key header', () => {
    setApplicantToken('token-abc')

    expect(Object.keys(applicantAuthHeaders())).not.toContain('X-Api-Key')
  })

  it('survives a reload by reading the token back from sessionStorage', () => {
    setApplicantToken('token-abc')

    // Simulate a fresh page: the module-level cache is gone but storage is not.
    clearApplicantToken()
    sessionStorage.setItem('waymark.applicantToken', 'token-abc')

    expect(getApplicantToken()).toBe('token-abc')
  })

  it('clears the token from storage', () => {
    setApplicantToken('token-abc')
    clearApplicantToken()

    expect(getApplicantToken()).toBeNull()
    expect(sessionStorage.getItem('waymark.applicantToken')).toBeNull()
  })

  it('keeps working when sessionStorage throws', () => {
    // Private-mode browsers and blocked site data both throw here; the journey must not break.
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('storage disabled')
    })
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('storage disabled')
    })

    expect(() => setApplicantToken('token-abc')).not.toThrow()
    expect(getApplicantToken()).toBe('token-abc')
  })

  describe('SSE query parameter', () => {
    it('appends the token, since EventSource cannot send headers', () => {
      setApplicantToken('token abc/=')

      expect(withApplicantTokenQuery('/api/workflow/sessions/1/events'))
        .toBe('/api/workflow/sessions/1/events?access_token=token%20abc%2F%3D')
    })

    it('uses & when the url already has a query string', () => {
      setApplicantToken('t')

      expect(withApplicantTokenQuery('/events?foo=1')).toBe('/events?foo=1&access_token=t')
    })

    it('leaves the url untouched when there is no credential', () => {
      expect(withApplicantTokenQuery('/events')).toBe('/events')
    })
  })
})
