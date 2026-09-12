/**
 * Holds the per-session applicant credential returned by POST /sessions/start.
 *
 * This replaces the shared API key the app used to ship in its own bundle. The token authorises
 * one session and carries no operator rights, so the worst a leaked one exposes is the journey its
 * holder was already completing.
 *
 * Stored in sessionStorage so a page reload mid-journey does not strand the applicant, and so the
 * credential dies with the tab. Every access is guarded: sessionStorage throws in private-mode
 * browsers and when site data is blocked, and losing the token must not break the journey in
 * progress — the in-memory copy still serves it.
 */
const STORAGE_KEY = 'waymark.applicantToken'

let inMemoryToken: string | null = null

export function setApplicantToken(token: string | null): void {
  inMemoryToken = token

  try {
    if (token === null) {
      sessionStorage.removeItem(STORAGE_KEY)
    } else {
      sessionStorage.setItem(STORAGE_KEY, token)
    }
  } catch {
    // Storage unavailable; the in-memory token covers this page view.
  }
}

export function getApplicantToken(): string | null {
  if (inMemoryToken !== null) return inMemoryToken

  try {
    inMemoryToken = sessionStorage.getItem(STORAGE_KEY)
  } catch {
    inMemoryToken = null
  }

  return inMemoryToken
}

export function clearApplicantToken(): void {
  setApplicantToken(null)
}

/** Authorization header for the current applicant session, or `{}` when there is none. */
export function applicantAuthHeaders(): Record<string, string> {
  const token = getApplicantToken()
  return token ? { Authorization: `Bearer ${token}` } : {}
}

/**
 * Appends the credential to a URL as a query parameter.
 *
 * Only for the SSE endpoint: the browser EventSource API cannot set request headers, so a query
 * parameter is the sole way to present a credential on that request.
 */
export function withApplicantTokenQuery(url: string): string {
  const token = getApplicantToken()
  if (!token) return url

  const separator = url.includes('?') ? '&' : '?'
  return `${url}${separator}access_token=${encodeURIComponent(token)}`
}
