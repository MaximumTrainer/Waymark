/**
 * The one place that says which routes need which role.
 *
 * Guarding was previously inline in App.tsx and covered a single path, so every operator surface
 * added afterwards rendered on the public applicant page by default. Here the default is the other
 * way round: an unknown path is treated as operator-only, and a route is public only by being
 * listed as such.
 */
export type RequiredRole = 'public' | 'Operator'

export interface RouteDefinition {
  /** Route id, used to pick the page component. */
  readonly id: 'applicant' | 'login' | 'admin-console' | 'admin-journey-builder'
  readonly requiredRole: RequiredRole
}

const ROUTES: ReadonlyArray<{ readonly match: (path: string) => boolean } & RouteDefinition> = [
  {
    id: 'login',
    requiredRole: 'public',
    match: (path) => path === '/login',
  },
  {
    id: 'admin-journey-builder',
    requiredRole: 'Operator',
    match: (path) => path.startsWith('/admin/journey-builder'),
  },
  {
    id: 'admin-console',
    requiredRole: 'Operator',
    match: (path) => path === '/admin' || path.startsWith('/admin/'),
  },
  {
    id: 'applicant',
    requiredRole: 'public',
    match: (path) => path === '/' || path === '',
  },
]

/**
 * An unmatched path under /admin must never fall through to the applicant page, and an unmatched
 * path elsewhere is a 404 that the applicant page renders. Anything beginning with /admin is
 * therefore operator-only even when no route matches it.
 */
const UNKNOWN_ADMIN: RouteDefinition = { id: 'admin-console', requiredRole: 'Operator' }
const UNKNOWN_PUBLIC: RouteDefinition = { id: 'applicant', requiredRole: 'public' }

export function resolveRoute(pathname: string): RouteDefinition {
  const path = normalize(pathname)

  const matched = ROUTES.find((route) => route.match(path))
  if (matched) return { id: matched.id, requiredRole: matched.requiredRole }

  return path.startsWith('/admin') ? UNKNOWN_ADMIN : UNKNOWN_PUBLIC
}

export function requiresOperator(pathname: string): boolean {
  return resolveRoute(pathname).requiredRole === 'Operator'
}

/** Trailing slashes are equivalent: /admin/ is the same route as /admin. */
function normalize(pathname: string): string {
  if (pathname.length > 1 && pathname.endsWith('/')) {
    return pathname.slice(0, -1)
  }
  return pathname
}
