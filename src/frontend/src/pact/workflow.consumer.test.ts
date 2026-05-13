import { describe, it, expect } from 'vitest'
import { PactV3, MatchersV3 } from '@pact-foundation/pact'
import { fileURLToPath } from 'url'
import { dirname, resolve } from 'path'
import { startSession, submitStep, getNextStep } from '../onboarding/api/workflow-api-client'

const { like, uuid, string } = MatchersV3

const __filename = fileURLToPath(import.meta.url)
const __dirname = dirname(__filename)

const provider = new PactV3({
  consumer: 'open-onboarding-frontend',
  provider: 'open-onboarding-api',
  dir: resolve(__dirname, '../../pacts'),
  logLevel: 'warn',
})

const TEST_API_KEY = 'test-api-key'
const FLOW_ID = '550e8400-e29b-41d4-a716-446655440000'
const NODE_ID = '660e8400-e29b-41d4-a716-446655440001'
const SESSION_ID = '770e8400-e29b-41d4-a716-446655440002'

const sessionStepResponseBody = {
  sessionId: uuid(),
  isCompleted: like(false),
  currentNode: like({
    id: uuid(),
    key: string('test-step'),
    type: string('Form'),
    title: string('Step Title'),
    jsonContent: string('{}'),
  }),
}

describe('Workflow API — consumer contract', () => {
  it('starts an onboarding session', async () => {
    await provider
      .addInteraction({
        states: [{ description: `a flow with id ${FLOW_ID} exists` }],
        uponReceiving: 'a request to start an onboarding session',
        withRequest: {
          method: 'POST',
          path: '/api/workflow/sessions/start',
          headers: {
            'X-Api-Key': TEST_API_KEY,
            'Content-Type': 'application/json',
          },
          body: { flowId: FLOW_ID },
        },
        willRespondWith: {
          status: 200,
          body: sessionStepResponseBody,
        },
      })
      .executeTest(async (mockServer) => {
        const result = await startSession(mockServer.url, { flowId: FLOW_ID }, TEST_API_KEY)
        expect(result.sessionId).toBeDefined()
        expect(result.isCompleted).toBe(false)
        expect(result.currentNode).not.toBeNull()
      })
  })

  it('submits a step in an onboarding session', async () => {
    await provider
      .addInteraction({
        states: [
          {
            description: `session ${SESSION_ID} is at step ${NODE_ID}`,
          },
        ],
        uponReceiving: 'a request to submit a step',
        withRequest: {
          method: 'POST',
          path: `/api/workflow/sessions/${SESSION_ID}/steps/${NODE_ID}/submit`,
          headers: {
            'X-Api-Key': TEST_API_KEY,
            'Content-Type': 'application/json',
          },
          body: { payload: like({ field: 'value' }) },
        },
        willRespondWith: {
          status: 200,
          body: sessionStepResponseBody,
        },
      })
      .executeTest(async (mockServer) => {
        const result = await submitStep(
          mockServer.url,
          SESSION_ID,
          NODE_ID,
          { payload: { field: 'value' } },
          TEST_API_KEY,
        )
        expect(result.sessionId).toBeDefined()
        expect(result.isCompleted).toBe(false)
      })
  })

  it('gets the next step in an onboarding session', async () => {
    await provider
      .addInteraction({
        states: [{ description: `session ${SESSION_ID} exists` }],
        uponReceiving: 'a request to get the next step',
        withRequest: {
          method: 'GET',
          path: `/api/workflow/sessions/${SESSION_ID}/next`,
          headers: {
            'X-Api-Key': TEST_API_KEY,
          },
        },
        willRespondWith: {
          status: 200,
          body: sessionStepResponseBody,
        },
      })
      .executeTest(async (mockServer) => {
        const result = await getNextStep(mockServer.url, SESSION_ID, TEST_API_KEY)
        expect(result.sessionId).toBeDefined()
        expect(result.isCompleted).toBeDefined()
      })
  })

  it('returns 400 when starting a session with missing flowId', async () => {
    await provider
      .addInteraction({
        states: [],
        uponReceiving: 'a request to start a session with missing flowId',
        withRequest: {
          method: 'POST',
          path: '/api/workflow/sessions/start',
          headers: {
            'X-Api-Key': TEST_API_KEY,
            'Content-Type': 'application/json',
          },
          body: {},
        },
        willRespondWith: {
          status: 400,
          body: {
            status: like(400),
            title: string('Validation failed'),
          },
        },
      })
      .executeTest(async (mockServer) => {
        await expect(
          startSession(mockServer.url, {} as Parameters<typeof startSession>[1], TEST_API_KEY),
        ).rejects.toThrow()
      })
  })
})
