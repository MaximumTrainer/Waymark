import { useCallback, useEffect, useState } from 'react'
import { AdminConsolePage } from './admin/AdminConsolePage'
import { AdminJourneyBuilderPage } from './builder/AdminJourneyBuilderPage'
import { ApplicantJourneyPage } from './onboarding/ApplicantJourneyPage'
import { resolveRoute } from './routing/routes'

type AuthMeResponse = {
  authenticated: boolean
  roles?: string[]
}

type AdminAuthState = 'checking' | 'authorized' | 'unauthorized'

/** An auth result is only meaningful for the path it was fetched for. */
type AdminAuthResult = { path: string; state: Exclude<AdminAuthState, 'checking'> }

const LOGIN_ERROR_MESSAGES: Record<string, string> = {
  saml_access_denied: 'Access Denied: User not authorized in IdP.',
  saml_certificate_expired: 'SAML certificate expired.',
  saml_invalid_assertion: 'Invalid SAML assertion.',
  saml_assertion_not_encrypted: 'SAML assertion was not encrypted.',
  saml_csrf_failed: 'SAML security validation failed. Please try again.',
  auth_check_failed: 'Could not validate your admin session. Please try again.',
}

const apiBase = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

function buildApiUrl(path: string) {
  return `${apiBase}${path}`
}

function shouldUseAbsoluteReturnUrl() {
  const apiOrigin = new URL(buildApiUrl('/api/auth/me'), window.location.origin).origin
  return apiOrigin !== window.location.origin
}

function buildReturnUrl(path: string) {
  return shouldUseAbsoluteReturnUrl() ? `${window.location.origin}${path}` : path
}

function App() {
  const [currentPath, setCurrentPath] = useState(() => window.location.pathname)
  const [currentSearch, setCurrentSearch] = useState(() => window.location.search)
  const [adminAuthResult, setAdminAuthResult] = useState<AdminAuthResult | null>(null)

  const route = resolveRoute(currentPath)
  const requiresOperatorRole = route.requiredRole === 'Operator'

  // Derived rather than stored, so a result fetched for a previous path can never be mistaken for
  // this one's - navigating to another admin route re-checks instead of reusing the old answer.
  const adminAuthState: AdminAuthState =
    adminAuthResult?.path === currentPath ? adminAuthResult.state : 'checking'

  useEffect(() => {
    const handlePopState = () => {
      setCurrentPath(window.location.pathname)
      setCurrentSearch(window.location.search)
    }

    window.addEventListener('popstate', handlePopState)
    return () => window.removeEventListener('popstate', handlePopState)
  }, [])

  const navigate = useCallback((path: string, replace = false) => {
    if (replace) {
      window.history.replaceState({}, '', path)
    } else {
      window.history.pushState({}, '', path)
    }
    setCurrentPath(window.location.pathname)
    setCurrentSearch(window.location.search)
  }, [])

  // Identity is checked only for routes that need it, so the public page makes no auth call.
  useEffect(() => {
    if (!requiresOperatorRole) return

    let cancelled = false

    fetch(buildApiUrl('/api/auth/me'), { credentials: 'include' })
      .then(async (response) => {
        if (!response.ok) throw new Error('auth_check_failed')
        return (await response.json()) as AuthMeResponse
      })
      .then((identity) => {
        if (cancelled) return
        const isAuthorized = identity.authenticated && (identity.roles ?? []).includes('Operator')
        if (!isAuthorized) {
          setAdminAuthResult({ path: currentPath, state: 'unauthorized' })
          navigate(`/login?returnUrl=${encodeURIComponent(buildReturnUrl(currentPath))}`, true)
          return
        }

        setAdminAuthResult({ path: currentPath, state: 'authorized' })
      })
      .catch(() => {
        if (cancelled) return
        setAdminAuthResult({ path: currentPath, state: 'unauthorized' })
        navigate(
          `/login?error=auth_check_failed&returnUrl=${encodeURIComponent(buildReturnUrl(currentPath))}`,
          true,
        )
      })

    return () => {
      cancelled = true
    }
  }, [currentPath, requiresOperatorRole, navigate])

  if (route.id === 'login') {
    const params = new URLSearchParams(currentSearch)
    const errorCode = params.get('error')
    const returnUrl = params.get('returnUrl') ?? buildReturnUrl('/admin')
    const message = errorCode ? (LOGIN_ERROR_MESSAGES[errorCode] ?? 'Authentication failed.') : null
    const loginUrl = `${buildApiUrl('/auth/saml/login')}?${new URLSearchParams({ returnUrl }).toString()}`

    return (
      <main className="mx-auto flex min-h-screen max-w-xl items-center p-6">
        <section className="w-full space-y-4 rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
          <h1 className="text-2xl font-bold text-slate-900">Admin Login</h1>
          <p className="text-sm text-slate-600">Sign in to access the Waymark operator console.</p>
          {message ? <p role="alert" className="rounded border border-rose-200 bg-rose-50 p-3 text-sm text-rose-700">{message}</p> : null}
          <button
            type="button"
            onClick={() => window.location.assign(loginUrl)}
            className="rounded bg-slate-900 px-4 py-2 text-sm font-medium text-white"
          >
            Login with SSO
          </button>
        </section>
      </main>
    )
  }

  if (requiresOperatorRole) {
    // Nothing operator-facing renders until the Operator role is confirmed. The redirect to /login
    // happens in the effect above; this is what the user sees until it does.
    if (adminAuthState !== 'authorized') {
      return (
        <main className="mx-auto max-w-xl p-6">
          <p className="text-sm text-slate-600">Checking admin session…</p>
        </main>
      )
    }

    return route.id === 'admin-journey-builder' ? <AdminJourneyBuilderPage /> : <AdminConsolePage />
  }

  return <ApplicantJourneyPage search={currentSearch} />
}

export default App
