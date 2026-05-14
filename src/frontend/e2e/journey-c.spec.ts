import { test, expect, type APIRequestContext } from '@playwright/test'

/**
 * API-level tests for Journey C – Compliance Heavy.
 * Verifies: national ID pattern validation, document upload progression,
 * and redirect node URL interpolation ({{sessionId}} replaced with real session ID).
 */

const API_BASE = process.env.PLAYWRIGHT_API_BASE_URL ?? 'http://localhost:5072'
const FLOW_ID = 'c3c3c3c3-c3c3-c3c3-c3c3-c3c3c3c3c3c3'
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

test.describe('Journey C – Compliance Heavy (API)', () => {
  test('rejects invalid NationalId format – returns 422 with pattern violation', async ({ request }) => {
    const session = await startSession(request)
    const { sessionId, currentNode } = session
    expect(currentNode.key).toBe('identity-verification')

    const res = await request.post(
      `${API_BASE}/api/workflow/sessions/${sessionId}/steps/${currentNode.id}/submit`,
      { headers: authHeaders, data: { NationalId: 'INVALID' } },
    )
    expect(res.status()).toBe(422)
    const body = await res.json() as { violations: Array<{ field: string; message: string }> }
    expect(body.violations.length).toBeGreaterThan(0)
    expect(body.violations.some((v) => v.field === 'NationalId')).toBe(true)
  })

  test('rejects missing NationalId – returns 422', async ({ request }) => {
    const session = await startSession(request)
    const { sessionId, currentNode } = session

    const res = await request.post(
      `${API_BASE}/api/workflow/sessions/${sessionId}/steps/${currentNode.id}/submit`,
      { headers: authHeaders, data: {} },
    )
    expect(res.status()).toBe(422)
    const body = await res.json() as { violations: Array<{ field: string }> }
    expect(body.violations.map((v) => v.field)).toContain('NationalId')
  })

  test('advances to DocumentUpload after valid NationalId', async ({ request }) => {
    const session = await startSession(request)
    const { sessionId, currentNode } = session

    // Format: 2 uppercase letters + 6 digits
    const res = await request.post(
      `${API_BASE}/api/workflow/sessions/${sessionId}/steps/${currentNode.id}/submit`,
      { headers: authHeaders, data: { NationalId: 'AB123456' } },
    )
    expect(res.status()).toBe(200)
    const next = await res.json() as { currentNode: { type: string; key: string } }
    expect(next.currentNode.type).toBe('DocumentUpload')
    expect(next.currentNode.key).toBe('id-document-upload')
  })

  test('advances to Redirect node after DocumentUpload submission', async ({ request }) => {
    const session = await startSession(request)
    const { sessionId, currentNode: identityNode } = session

    // Step 1: submit valid identity
    const step1 = await request.post(
      `${API_BASE}/api/workflow/sessions/${sessionId}/steps/${identityNode.id}/submit`,
      { headers: authHeaders, data: { NationalId: 'CD789012' } },
    )
    expect(step1.status()).toBe(200)
    const afterIdentity = await step1.json() as { currentNode: { id: string; type: string } }
    expect(afterIdentity.currentNode.type).toBe('DocumentUpload')

    // Step 2: submit DocumentUpload with empty payload (no file scanning in test mode)
    const step2 = await request.post(
      `${API_BASE}/api/workflow/sessions/${sessionId}/steps/${afterIdentity.currentNode.id}/submit`,
      { headers: authHeaders, data: {} },
    )
    expect(step2.status()).toBe(200)
    const afterUpload = await step2.json() as { currentNode: { type: string; key: string } }
    expect(afterUpload.currentNode.type).toBe('Redirect')
    expect(afterUpload.currentNode.key).toBe('external-verification')
  })

  test('Redirect node URL contains real session ID (not literal template token)', async ({ request }) => {
    const session = await startSession(request)
    const { sessionId, currentNode: identityNode } = session

    // Advance through identity + document upload steps
    const step1 = await request.post(
      `${API_BASE}/api/workflow/sessions/${sessionId}/steps/${identityNode.id}/submit`,
      { headers: authHeaders, data: { NationalId: 'EF345678' } },
    )
    const afterIdentity = await step1.json() as { currentNode: { id: string } }

    const step2 = await request.post(
      `${API_BASE}/api/workflow/sessions/${sessionId}/steps/${afterIdentity.currentNode.id}/submit`,
      { headers: authHeaders, data: {} },
    )
    const afterUpload = await step2.json() as {
      currentNode: { type: string; jsonContent: string }
    }

    expect(afterUpload.currentNode.type).toBe('Redirect')

    const content = JSON.parse(afterUpload.currentNode.jsonContent) as { url?: string }
    expect(content.url).toBeDefined()
    expect(content.url).toContain(sessionId)
    expect(content.url).not.toContain('{{sessionId}}')
  })
})
