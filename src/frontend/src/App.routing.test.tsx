import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'

/**
 * The public applicant page must contain no operator surface at all: not the data, not the
 * components, and not the requests that fetch them.
 */
describe('App routing and route guards', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  function setPath(path: string, search = '') {
    window.history.replaceState({}, '', `${path}${search}`)
  }

  beforeEach(() => {
    fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString()

      if (url.includes('/api/workflow/sessions/start')) {
        return new Response(
          JSON.stringify({
            sessionId: 'session-1',
            isCompleted: false,
            currentNode: {
              id: 'node-1',
              key: 'start',
              type: 'Information',
              title: 'Welcome aboard',
              jsonContent: JSON.stringify({ message: 'Hello' }),
            },
            applicantToken: 'applicant-token',
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        )
      }

      if (url.includes('/api/auth/me')) {
        return new Response(JSON.stringify({ authenticated: false }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      }

      return new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } })
    })

    vi.stubGlobal('fetch', fetchMock)
    vi.stubGlobal('EventSource', undefined)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    setPath('/')
  })

  function requestedUrls(): string[] {
    return fetchMock.mock.calls.map(([input]) =>
      typeof input === 'string' ? input : String(input),
    )
  }

  describe('public applicant route', () => {
    beforeEach(() => setPath('/'))

    it('renders the onboarding step and nothing operator-facing', async () => {
      render(<App />)

      await screen.findByText('Welcome aboard')

      // Each of these headings belongs to an operator surface that used to render here.
      for (const heading of [
        /Journey Dashboard/i,
        /Flow Analytics/i,
        /Session History/i,
        /Webhook Deliveries/i,
        /Flow Authoring/i,
        /Version History/i,
        /Operator Console/i,
      ]) {
        expect(screen.queryByText(heading)).toBeNull()
      }
    })

    it('does not render the journey or persona selectors', async () => {
      render(<App />)
      await screen.findByText('Welcome aboard')

      expect(document.querySelector('#journey-select')).toBeNull()
      expect(document.querySelector('#persona-select')).toBeNull()
    })

    it('requests no operator-only endpoint', async () => {
      render(<App />)
      await screen.findByText('Welcome aboard')

      const operatorPaths = [
        '/api/workflow/sessions?',
        '/api/webhooks',
        '/api/flows/',
        '/api/analytics/',
        '/api/customers',
        '/api/auth/me',
      ]

      for (const path of operatorPaths) {
        expect(requestedUrls().filter((url) => url.includes(path))).toEqual([])
      }
    })

    it('starts the journey named by the link', async () => {
      setPath('/', '?flowId=22222222-2222-2222-2222-222222222222')
      render(<App />)

      await waitFor(() => {
        const startCall = fetchMock.mock.calls.find(([input]) =>
          String(input).includes('/sessions/start'),
        )
        expect(startCall).toBeDefined()
        expect(String(startCall![1]?.body)).toContain('22222222-2222-2222-2222-222222222222')
      })
    })
  })

  describe('operator routes', () => {
    it.each(['/admin', '/admin/journey-builder', '/admin/not-built-yet'])(
      'redirects an unauthenticated visitor from %s to /login without rendering operator data',
      async (path) => {
        setPath(path)
        render(<App />)

        await waitFor(() => expect(window.location.pathname).toBe('/login'))

        // Headings, not free text: the login page legitimately mentions the operator console.
        expect(screen.queryByRole('heading', { name: /Waymark Operator Console/i })).toBeNull()
        expect(screen.queryByRole('heading', { name: /Session History/i })).toBeNull()
        expect(screen.queryByRole('heading', { name: /Webhook Deliveries/i })).toBeNull()
      },
    )

    it('renders the operator console once the Operator role is confirmed', async () => {
      fetchMock.mockImplementation(async (input: RequestInfo | URL) => {
        const url = String(input)
        if (url.includes('/api/auth/me')) {
          return new Response(
            JSON.stringify({ authenticated: true, roles: ['Operator'] }),
            { status: 200, headers: { 'Content-Type': 'application/json' } },
          )
        }
        return new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } })
      })

      setPath('/admin')
      render(<App />)

      expect(
        await screen.findByRole('heading', { name: /Waymark Operator Console/i }),
      ).toBeInTheDocument()
    })

    it('shows the checking state and no operator data while the role is unconfirmed', async () => {
      // Auth never resolves: the console must not render in the meantime.
      fetchMock.mockImplementation(
        (input: RequestInfo | URL) =>
          String(input).includes('/api/auth/me')
            ? new Promise<Response>(() => {})
            : Promise.resolve(new Response('[]', { status: 200 })),
      )

      setPath('/admin')
      render(<App />)

      expect(screen.getByText(/Checking admin session/i)).toBeInTheDocument()
      expect(screen.queryByRole('heading', { name: /Waymark Operator Console/i })).toBeNull()
    })
  })
})
