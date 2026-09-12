/**
 * `fetch` for operator-only endpoints.
 *
 * Operator surfaces authenticate with the `AdminSession` cookie established by SAML SSO, which is
 * why `credentials: 'include'` is set here rather than left to each call site. They must never send
 * an API key: `X-Api-Key` maps to a full Operator principal, and a key the browser holds is a key
 * every visitor holds.
 */
export function operatorFetch(input: string, init: RequestInit = {}): Promise<Response> {
  return fetch(input, { ...init, credentials: 'include' })
}

/** Headers for an operator request with a JSON body. */
export function operatorJsonHeaders(): Record<string, string> {
  return { 'Content-Type': 'application/json' }
}
