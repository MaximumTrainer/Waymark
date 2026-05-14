import { test, expect, type APIRequestContext } from '@playwright/test'

/**
 * API-level tests for Journey B – Conditional Branch.
 * Verifies that the routing engine correctly directs EU applicants to the
 * GDPR disclosure node and all other applicants to the global terms node.
 */

const API_BASE = process.env.PLAYWRIGHT_API_BASE_URL ?? 'http://localhost:5072'
const FLOW_ID = 'b2b2b2b2-b2b2-b2b2-b2b2-b2b2b2b2b2b2'
const API_KEY = process.env.PLAYWRIGHT_API_KEY ?? 'playwright-api-key'

const authHeaders = { 'X-Api-Key': API_KEY, 'Content-Type': 'application/json' }

async function startSession(request: APIRequestContext) {
  const res = await request.post(`${API_BASE}/api/workflow/sessions/start`, {
    headers: authHeaders,
    data: { flowId: FLOW_ID },
  })
  expect(res.status()).toBe(200)
  return res.json() as Promise<{
    sessionId: string
    isCompleted: boolean
    currentNode: { id: string; type: string; key: string }
  }>
}

test.describe('Journey B – Conditional Branch (API)', () => {
  test('routes France to EU GDPR Disclosure node', async ({ request }) => {
    const session = await startSession(request)
    const { sessionId, currentNode } = session
    expect(currentNode.type).toBe('Form')

    const res = await request.post(
      `${API_BASE}/api/workflow/sessions/${sessionId}/steps/${currentNode.id}/submit`,
      { headers: authHeaders, data: { payload: { Country: 'France' } } },
    )
    expect(res.status()).toBe(200)
    const next = await res.json() as { currentNode: { key: string; type: string } }
    expect(next.currentNode.type).toBe('Information')
    expect(next.currentNode.key).toContain('eu')
  })

  test('routes Germany to EU GDPR Disclosure node', async ({ request }) => {
    const session = await startSession(request)
    const { sessionId, currentNode } = session

    const res = await request.post(
      `${API_BASE}/api/workflow/sessions/${sessionId}/steps/${currentNode.id}/submit`,
      { headers: authHeaders, data: { payload: { Country: 'Germany' } } },
    )
    expect(res.status()).toBe(200)
    const next = await res.json() as { currentNode: { key: string; type: string } }
    expect(next.currentNode.type).toBe('Information')
    expect(next.currentNode.key).toContain('eu')
  })

  test('routes USA to Global Terms node', async ({ request }) => {
    const session = await startSession(request)
    const { sessionId, currentNode } = session

    const res = await request.post(
      `${API_BASE}/api/workflow/sessions/${sessionId}/steps/${currentNode.id}/submit`,
      { headers: authHeaders, data: { payload: { Country: 'USA' } } },
    )
    expect(res.status()).toBe(200)
    const next = await res.json() as { currentNode: { key: string; type: string } }
    expect(next.currentNode.type).toBe('Information')
    expect(next.currentNode.key).toContain('global')
  })

  test('routes Other to Global Terms node (fallback)', async ({ request }) => {
    const session = await startSession(request)
    const { sessionId, currentNode } = session

    const res = await request.post(
      `${API_BASE}/api/workflow/sessions/${sessionId}/steps/${currentNode.id}/submit`,
      { headers: authHeaders, data: { payload: { Country: 'Other' } } },
    )
    expect(res.status()).toBe(200)
    const next = await res.json() as { currentNode: { key: string; type: string } }
    expect(next.currentNode.type).toBe('Information')
    expect(next.currentNode.key).toContain('global')
  })

  test('rejects missing Country field – returns 422', async ({ request }) => {
    const session = await startSession(request)
    const { sessionId, currentNode } = session

    const res = await request.post(
      `${API_BASE}/api/workflow/sessions/${sessionId}/steps/${currentNode.id}/submit`,
      { headers: authHeaders, data: { payload: {} } },
    )
    expect(res.status()).toBe(422)
    const body = await res.json() as { violations: Array<{ field: string }> }
    expect(body.violations.map((v) => v.field)).toContain('Country')
  })
})
