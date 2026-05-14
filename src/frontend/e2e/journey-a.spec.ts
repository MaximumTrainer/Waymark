import { test, expect } from '@playwright/test'

/**
 * API-level tests for Journey A – Linear Basic.
 * Uses the Playwright `request` fixture to call the backend directly.
 * Requires the backend to be running with DataSeeder flows seeded.
 */

const API_BASE = process.env.PLAYWRIGHT_API_BASE_URL ?? 'http://localhost:5072'
const FLOW_ID = 'a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1'
const API_KEY = process.env.PLAYWRIGHT_API_KEY ?? 'playwright-api-key'

const authHeaders = { 'X-Api-Key': API_KEY, 'Content-Type': 'application/json' }

test.describe('Journey A – Linear Basic (API)', () => {
  test('completes full linear journey: form → information → completed', async ({ request }) => {
    // Start session
    const startRes = await request.post(`${API_BASE}/api/workflow/sessions/start`, {
      headers: authHeaders,
      data: { flowId: FLOW_ID },
    })
    expect(startRes.status()).toBe(200)
    const start = await startRes.json() as {
      sessionId: string
      isCompleted: boolean
      currentNode: { id: string; type: string; title: string }
    }
    expect(start.isCompleted).toBe(false)
    expect(start.currentNode.type).toBe('Form')
    expect(start.currentNode.title).toMatch(/contact/i)

    const sessionId = start.sessionId
    const formNodeId = start.currentNode.id

    // Submit form with valid data
    const submitRes = await request.post(
      `${API_BASE}/api/workflow/sessions/${sessionId}/steps/${formNodeId}/submit`,
      {
        headers: authHeaders,
        data: { payload: { FullName: 'Alice Example', Email: 'alice@example.com' } },
      },
    )
    expect(submitRes.status()).toBe(200)
    const afterForm = await submitRes.json() as {
      sessionId: string
      isCompleted: boolean
      currentNode: { id: string; type: string; key: string }
    }
    expect(afterForm.isCompleted).toBe(false)
    expect(afterForm.currentNode.type).toBe('Information')

    const infoNodeId = afterForm.currentNode.id

    // Submit Information node (empty payload advances the session)
    const infoRes = await request.post(
      `${API_BASE}/api/workflow/sessions/${sessionId}/steps/${infoNodeId}/submit`,
      {
        headers: authHeaders,
        data: { payload: {} },
      },
    )
    expect(infoRes.status()).toBe(200)
    const afterInfo = await infoRes.json() as { isCompleted: boolean; currentNode: null }
    expect(afterInfo.isCompleted).toBe(true)
    expect(afterInfo.currentNode).toBeNull()
  })

  test('rejects form submission with missing required fields – returns 422', async ({ request }) => {
    const startRes = await request.post(`${API_BASE}/api/workflow/sessions/start`, {
      headers: authHeaders,
      data: { flowId: FLOW_ID },
    })
    expect(startRes.status()).toBe(200)
    const start = await startRes.json() as { sessionId: string; currentNode: { id: string } }
    const { sessionId, currentNode } = start

    const res = await request.post(
      `${API_BASE}/api/workflow/sessions/${sessionId}/steps/${currentNode.id}/submit`,
      {
        headers: authHeaders,
        data: { payload: {} },
      },
    )
    expect(res.status()).toBe(422)
    const body = await res.json() as { violations: Array<{ field: string; message: string }> }
    expect(body.violations).toBeDefined()
    expect(body.violations.length).toBeGreaterThan(0)

    const fields = body.violations.map((v) => v.field)
    expect(fields).toContain('FullName')
    expect(fields).toContain('Email')
  })

  test('rejects form with invalid email format – returns 422 with Email violation', async ({ request }) => {
    const startRes = await request.post(`${API_BASE}/api/workflow/sessions/start`, {
      headers: authHeaders,
      data: { flowId: FLOW_ID },
    })
    expect(startRes.status()).toBe(200)
    const start = await startRes.json() as { sessionId: string; currentNode: { id: string } }

    const res = await request.post(
      `${API_BASE}/api/workflow/sessions/${start.sessionId}/steps/${start.currentNode.id}/submit`,
      {
        headers: authHeaders,
        data: { payload: { FullName: 'Bob Smith', Email: 'not-an-email' } },
      },
    )
    expect(res.status()).toBe(422)
    const body = await res.json() as { violations: Array<{ field: string }> }
    const fields = body.violations.map((v) => v.field)
    expect(fields).toContain('Email')
  })
})
