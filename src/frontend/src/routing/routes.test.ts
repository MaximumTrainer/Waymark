import { describe, expect, it } from 'vitest'
import { requiresOperator, resolveRoute } from './routes'

describe('route table', () => {
  it('serves the applicant journey at the root', () => {
    expect(resolveRoute('/')).toEqual({ id: 'applicant', requiredRole: 'public' })
  })

  it('leaves the login page public', () => {
    expect(resolveRoute('/login')).toEqual({ id: 'login', requiredRole: 'public' })
  })

  it.each([
    '/admin',
    '/admin/',
    '/admin/journey-builder',
    '/admin/journey-builder/nested',
    '/admin/sessions',
  ])('requires an operator for %s', (path) => {
    expect(requiresOperator(path)).toBe(true)
  })

  it.each(['/', '/login', '/anything-else'])('does not require an operator for %s', (path) => {
    expect(requiresOperator(path)).toBe(false)
  })

  it('picks the journey builder over the general admin console', () => {
    expect(resolveRoute('/admin/journey-builder').id).toBe('admin-journey-builder')
    expect(resolveRoute('/admin').id).toBe('admin-console')
  })

  it('treats an unknown admin path as operator-only rather than falling through to the applicant page', () => {
    // The regression this guards: a new admin surface added without a route entry must not end up
    // rendering on the public page.
    expect(resolveRoute('/admin/not-built-yet')).toEqual({
      id: 'admin-console',
      requiredRole: 'Operator',
    })
  })

  it('renders an unknown non-admin path as the applicant page', () => {
    expect(resolveRoute('/typo')).toEqual({ id: 'applicant', requiredRole: 'public' })
  })
})
