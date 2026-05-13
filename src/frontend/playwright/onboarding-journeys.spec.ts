import { expect, test } from '@playwright/test'

type MockNode = {
  id: string
  key: string
  type: 'Form' | 'DocumentUpload' | 'Information' | 'Logic' | 'Redirect'
  title: string
  jsonContent: string
  isStartNode: boolean
}

type MockJourney = {
  id: string
  name: string
  description: string
  nodes: MockNode[]
  connections: Array<{
    id: string
    sourceNodeId: string
    targetNodeId: string
    priority: number
  }>
}

const mockJourneys: Record<string, MockJourney> = {
  '11111111-1111-1111-1111-111111111111': {
    id: '11111111-1111-1111-1111-111111111111',
    name: 'Small business onboarding',
    description: 'Small business journey',
    nodes: [
      {
        id: 'small-node-1',
        key: 'small-business-details',
        type: 'Form',
        title: 'Small business details',
        jsonContent:
          '{"fields":[{"name":"BusinessName","type":"text","required":true},{"name":"BusinessAddress","type":"textarea","required":true},{"name":"AnnualRevenue","type":"number","required":true},{"name":"BusinessOwner","type":"text","required":true},{"name":"SanctionsDeclarationConfirmed","type":"checkbox","required":true}]}',
        isStartNode: true,
      },
      {
        id: 'small-node-2',
        key: 'small-complete',
        type: 'Information',
        title: 'Small business onboarding checks complete.',
        jsonContent: '{}',
        isStartNode: false,
      },
    ],
    connections: [
      {
        id: 'small-conn-1',
        sourceNodeId: 'small-node-1',
        targetNodeId: 'small-node-2',
        priority: 0,
      },
    ],
  },
  '22222222-2222-2222-2222-222222222222': {
    id: '22222222-2222-2222-2222-222222222222',
    name: 'Medium business onboarding',
    description: 'Medium business journey',
    nodes: [
      {
        id: 'medium-node-1',
        key: 'medium-business-details',
        type: 'Form',
        title: 'Medium business details',
        jsonContent:
          '{"fields":[{"name":"BusinessName","type":"text","required":true},{"name":"PrimaryAddress","type":"textarea","required":true},{"name":"AnnualRevenue","type":"number","required":true},{"name":"BusinessOwner","type":"text","required":true},{"name":"SecondaryBusinessOwner","type":"text","required":true},{"name":"NumberOfOutlets","type":"number","required":true},{"name":"BusinessStaffCount","type":"number","required":true},{"name":"BeneficialOwnersConfirmed","type":"checkbox","required":true},{"name":"TaxComplianceConfirmed","type":"checkbox","required":true}]}',
        isStartNode: true,
      },
      {
        id: 'medium-node-2',
        key: 'medium-document-verification',
        type: 'DocumentUpload',
        title: 'Upload ownership and trading documents',
        jsonContent:
          '{"acceptedFileTypes":["application/pdf","image/jpeg","image/png"],"maxFiles":2}',
        isStartNode: false,
      },
    ],
    connections: [
      {
        id: 'medium-conn-1',
        sourceNodeId: 'medium-node-1',
        targetNodeId: 'medium-node-2',
        priority: 0,
      },
    ],
  },
  '33333333-3333-3333-3333-333333333333': {
    id: '33333333-3333-3333-3333-333333333333',
    name: 'Large nationwide business onboarding',
    description: 'Large business journey',
    nodes: [
      {
        id: 'large-node-1',
        key: 'large-business-profile',
        type: 'Form',
        title: 'Large business profile',
        jsonContent:
          '{"fields":[{"name":"BusinessName","type":"text","required":true},{"name":"LegalStructure","type":"select","required":true,"options":["LimitedCompany","Partnership","PublicLimitedCompany","Other"]},{"name":"PrimaryAddress","type":"textarea","required":true},{"name":"AnnualRevenue","type":"number","required":true},{"name":"BusinessOwner","type":"text","required":true},{"name":"SecondaryBusinessOwner","type":"text","required":true},{"name":"NumberOfOutlets","type":"number","required":true},{"name":"BusinessStaffCount","type":"number","required":true}]}',
        isStartNode: true,
      },
      {
        id: 'large-node-2',
        key: 'large-compliance-questionnaire',
        type: 'Form',
        title: 'Compliance questionnaire',
        jsonContent:
          '{"fields":[{"name":"RegulatoryLicensesConfirmed","type":"checkbox","required":true},{"name":"SanctionsScreeningCompleted","type":"checkbox","required":true},{"name":"BeneficialOwnershipReviewed","type":"checkbox","required":true}]}',
        isStartNode: false,
      },
      {
        id: 'large-node-3',
        key: 'large-complete',
        type: 'Information',
        title: 'Large nationwide business onboarding checks complete.',
        jsonContent: '{}',
        isStartNode: false,
      },
    ],
    connections: [
      {
        id: 'large-conn-1',
        sourceNodeId: 'large-node-1',
        targetNodeId: 'large-node-2',
        priority: 0,
      },
      {
        id: 'large-conn-2',
        sourceNodeId: 'large-node-2',
        targetNodeId: 'large-node-3',
        priority: 0,
      },
    ],
  },
}

test.beforeEach(async ({ page }) => {
  const sessions = new Map<string, { journeyId: string; nodeIndex: number }>()

  await page.route('**/api/flows/*', async (route) => {
    const flowId = route.request().url().split('/').pop()!
    const journey = mockJourneys[flowId]

    if (!journey) {
      await route.fulfill({ status: 404, body: 'Flow not found' })
      return
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(journey),
    })
  })

  await page.route('**/api/workflow/sessions/start', async (route) => {
    const body = route.request().postDataJSON() as { flowId: string }
    const journey = mockJourneys[body.flowId]

    if (!journey) {
      await route.fulfill({ status: 400, body: 'Unknown flow' })
      return
    }

    const sessionId = `session-${Math.random().toString(36).slice(2)}`
    sessions.set(sessionId, { journeyId: journey.id, nodeIndex: 0 })

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        sessionId,
        isCompleted: false,
        currentNode: journey.nodes[0],
      }),
    })
  })

  await page.route('**/api/workflow/sessions/*/steps/*/submit', async (route) => {
    const match = route.request().url().match(/\/api\/workflow\/sessions\/([^/]+)\/steps\//)
    const sessionId = match?.[1]

    if (!sessionId || !sessions.has(sessionId)) {
      await route.fulfill({ status: 404, body: 'Session not found' })
      return
    }

    const state = sessions.get(sessionId)!
    const journey = mockJourneys[state.journeyId]
    const nextIndex = state.nodeIndex + 1
    const nextNode = journey.nodes[nextIndex] ?? null

    if (nextNode) {
      state.nodeIndex = nextIndex
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          sessionId,
          isCompleted: false,
          currentNode: nextNode,
        }),
      })
      return
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        sessionId,
        isCompleted: true,
        currentNode: null,
      }),
    })
  })

  await page.route('**/api/workflow/sessions/*/events', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'text/event-stream',
      body: '',
    })
  })
})

test('journey 1 runs simple small business onboarding flow', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByRole('heading', { name: 'Small business details' })).toBeVisible()

  await page.getByLabel('BusinessName').fill('Acme Local Ltd')
  await page.getByLabel('BusinessAddress').fill('1 High Street, London')
  await page.getByLabel('AnnualRevenue').fill('250000')
  await page.getByLabel('BusinessOwner').fill('Jamie Owner')
  await page.getByLabel('SanctionsDeclarationConfirmed').check()
  await page.getByRole('button', { name: 'Submit' }).click()

  await expect(page.getByText('Small business onboarding checks complete.')).toBeVisible()
})

test('journey 2 reaches medium business document verification step', async ({ page }) => {
  await page.goto('/')

  await page.selectOption('#journey-select', '22222222-2222-2222-2222-222222222222')
  await expect(page.getByRole('heading', { name: 'Medium business details' })).toBeVisible()

  await page.getByLabel('BusinessName').fill('Acme Regional Ltd')
  await page.getByLabel('PrimaryAddress').fill('200 King Road, Manchester')
  await page.getByLabel('AnnualRevenue').fill('3500000')
  await page.getByLabel('BusinessOwner').fill('Jordan Owner')
  await page.getByLabel('SecondaryBusinessOwner').fill('Taylor Coowner')
  await page.getByLabel('NumberOfOutlets').fill('18')
  await page.getByLabel('BusinessStaffCount').fill('120')
  await page.getByLabel('BeneficialOwnersConfirmed').check()
  await page.getByLabel('TaxComplianceConfirmed').check()
  await page.getByRole('button', { name: 'Submit' }).click()

  await expect(page.getByText('Upload ownership and trading documents')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Continue' })).toBeDisabled()
})

test('journey 3 runs large nationwide onboarding and compliance questions', async ({ page }) => {
  await page.goto('/')

  await page.selectOption('#journey-select', '33333333-3333-3333-3333-333333333333')
  await expect(page.getByRole('heading', { name: 'Large business profile' })).toBeVisible()

  await page.getByLabel('BusinessName').fill('Acme National PLC')
  await page.getByLabel('LegalStructure').selectOption('PublicLimitedCompany')
  await page.getByLabel('PrimaryAddress').fill('500 Enterprise Way, Birmingham')
  await page.getByLabel('AnnualRevenue').fill('25000000')
  await page.getByLabel('BusinessOwner').fill('Alex Executive')
  await page.getByLabel('SecondaryBusinessOwner').fill('Sam Director')
  await page.getByLabel('NumberOfOutlets').fill('220')
  await page.getByLabel('BusinessStaffCount').fill('2500')
  await page.getByRole('button', { name: 'Submit' }).click()

  await expect(page.getByRole('heading', { name: 'Compliance questionnaire' })).toBeVisible()
  await page.getByLabel('RegulatoryLicensesConfirmed').check()
  await page.getByLabel('SanctionsScreeningCompleted').check()
  await page.getByLabel('BeneficialOwnershipReviewed').check()
  await page.getByRole('button', { name: 'Submit' }).click()

  await expect(page.getByText('Large nationwide business onboarding checks complete.')).toBeVisible()
})
