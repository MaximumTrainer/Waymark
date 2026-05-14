import { test, expect, type Page } from '@playwright/test'

/**
 * Browser tests for individual StepRenderer node types.
 * All API calls are intercepted via page.route() so no live backend is required.
 */

const FLOW_ID = 'a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1'
const SESSION_ID = 'test-session-renderer-00000000'

function mockFlowList(page: Page) {
  return page.route('**/api/flows', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        items: [
          {
            id: FLOW_ID,
            name: 'Journey A – Linear Basic',
            description: '',
            version: 1,
            nodes: [],
            connections: [],
          },
        ],
        totalCount: 1,
        page: 1,
        pageSize: 20,
      }),
    }),
  )
}

function mockSSE(page: Page) {
  return page.route('**/api/workflow/sessions/*/events', (route) =>
    route.fulfill({ status: 200, contentType: 'text/event-stream', body: '' }),
  )
}

test.describe('StepRenderer – Information node', () => {
  test('renders title, message, and Continue button', async ({ page }) => {
    await mockFlowList(page)
    await page.route(`**/api/flows/${FLOW_ID}`, (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: '{}' }),
    )
    await page.route('**/api/workflow/sessions/start', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          sessionId: SESSION_ID,
          isCompleted: false,
          currentNode: {
            id: 'node-info-01',
            key: 'basic-confirmation',
            type: 'Information',
            title: 'Application received',
            jsonContent: JSON.stringify({ message: 'Thank you for applying.' }),
          },
        }),
      }),
    )

    await mockSSE(page)
    await page.goto('/')

    await expect(page.getByText('Application received')).toBeVisible()
    await expect(page.getByText('Thank you for applying.')).toBeVisible()
    await expect(page.getByRole('button', { name: /continue/i })).toBeVisible()
  })

  test('submits empty payload when Continue is clicked', async ({ page }) => {
    await mockFlowList(page)
    await page.route(`**/api/flows/${FLOW_ID}`, (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: '{}' }),
    )
    await page.route('**/api/workflow/sessions/start', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          sessionId: SESSION_ID,
          isCompleted: false,
          currentNode: {
            id: 'node-info-01',
            key: 'basic-confirmation',
            type: 'Information',
            title: 'Application received',
            jsonContent: JSON.stringify({ message: 'Thank you.' }),
          },
        }),
      }),
    )

    let submitBody: string | null = null
    await page.route(`**/api/workflow/sessions/${SESSION_ID}/steps/node-info-01/submit`, (route) => {
      submitBody = route.request().postData()
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ sessionId: SESSION_ID, isCompleted: true, currentNode: null }),
      })
    })

    await mockSSE(page)
    await page.goto('/')
    await page.getByRole('button', { name: /continue/i }).click()

    await expect(page.getByText(/all steps finished/i)).toBeVisible()
    const parsed = JSON.parse(submitBody ?? '{}') as Record<string, unknown>
    expect(parsed).toEqual({ payload: {} })
  })
})

test.describe('StepRenderer – Redirect node', () => {
  test('renders redirect link with interpolated URL', async ({ page }) => {
    await mockFlowList(page)
    await page.route(`**/api/flows/${FLOW_ID}`, (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: '{}' }),
    )
    const interpolatedUrl = `https://verify.example.com/start?session=${SESSION_ID}`
    await page.route('**/api/workflow/sessions/start', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          sessionId: SESSION_ID,
          isCompleted: false,
          currentNode: {
            id: 'node-redirect-01',
            key: 'external-verification',
            type: 'Redirect',
            title: 'External verification',
            jsonContent: JSON.stringify({
              url: interpolatedUrl,
              message: 'Redirecting to partner.',
            }),
          },
        }),
      }),
    )

    await mockSSE(page)
    await page.goto('/')

    const link = page.getByRole('link', { name: /continue to external provider/i })
    await expect(link).toBeVisible()
    const href = await link.getAttribute('href')
    expect(href).toContain(SESSION_ID)
  })
})

test.describe('StepRenderer – Form node with select field', () => {
  test('renders select options from jsonContent', async ({ page }) => {
    await mockFlowList(page)
    await page.route(`**/api/flows/${FLOW_ID}`, (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: '{}' }),
    )
    await page.route('**/api/workflow/sessions/start', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          sessionId: SESSION_ID,
          isCompleted: false,
          currentNode: {
            id: 'node-form-01',
            key: 'country-selection',
            type: 'Form',
            title: 'Country selection',
            jsonContent: JSON.stringify({
              fields: [
                {
                  name: 'Country',
                  type: 'select',
                  required: true,
                  options: ['France', 'Germany', 'USA', 'Other'],
                },
              ],
            }),
          },
        }),
      }),
    )

    await mockSSE(page)
    await page.goto('/')

    await expect(page.getByLabel('Country')).toBeVisible()
    await expect(page.getByRole('option', { name: 'France' })).toBeAttached()
    await expect(page.getByRole('option', { name: 'Germany' })).toBeAttached()
    await expect(page.getByRole('option', { name: 'USA' })).toBeAttached()
  })
})

test.describe('StepRenderer – DocumentUpload node', () => {
  test('renders file input', async ({ page }) => {
    await mockFlowList(page)
    await page.route(`**/api/flows/${FLOW_ID}`, (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: '{}' }),
    )
    await page.route('**/api/workflow/sessions/start', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          sessionId: SESSION_ID,
          isCompleted: false,
          currentNode: {
            id: 'node-upload-01',
            key: 'id-document-upload',
            type: 'DocumentUpload',
            title: 'Upload identity document',
            jsonContent: JSON.stringify({
              acceptedFileTypes: ['application/pdf', 'image/png'],
              maxFiles: 1,
              instructions: 'Upload a clear scan.',
            }),
          },
        }),
      }),
    )

    await mockSSE(page)
    await page.goto('/')

    await expect(page.locator('input[type="file"]')).toBeVisible()
    await expect(page.getByText('Upload identity document')).toBeVisible()
  })
})

test.describe('StepRenderer – Logic node', () => {
  test('renders processing spinner', async ({ page }) => {
    await mockFlowList(page)
    await page.route(`**/api/flows/${FLOW_ID}`, (route) =>
      route.fulfill({ status: 200, contentType: 'application/json', body: '{}' }),
    )
    await page.route('**/api/workflow/sessions/start', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          sessionId: SESSION_ID,
          isCompleted: false,
          currentNode: {
            id: 'node-logic-01',
            key: 'auto-routing',
            type: 'Logic',
            title: 'Processing',
            jsonContent: '{}',
          },
        }),
      }),
    )

    await mockSSE(page)
    await page.goto('/')

    await expect(page.getByText(/processing/i)).toBeVisible()
    await expect(page.locator('svg.animate-spin')).toBeVisible()
  })
})
