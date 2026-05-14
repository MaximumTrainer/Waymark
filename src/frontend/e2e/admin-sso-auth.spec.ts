import { expect, test } from '@playwright/test'

test.describe('Admin SSO auth guard', () => {
  test('happy path: admin URL -> SSO login -> redirects to journey builder', async ({ page }) => {
    await page.route('**/api/auth/me', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ authenticated: true, roles: ['Operator'] }),
      })
    })

    await page.route('**/auth/saml/login**', async (route) => {
      await route.fulfill({
        status: 302,
        headers: {
          location: '/admin/journey-builder',
        },
      })
    })

    await page.goto('/login?returnUrl=%2Fadmin%2Fjourney-builder')
    await page.getByRole('button', { name: 'Login with SSO' }).click()
    await expect(page).toHaveURL(/\/admin\/journey-builder/)
  })

  test('secure access: direct admin route without session redirects to login', async ({ page }) => {
    await page.route('**/api/auth/me', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ authenticated: false }),
      })
    })

    await page.goto('/admin/journey-builder')
    await expect(page).toHaveURL(/\/login\?returnUrl=/)
    await expect(page.getByRole('button', { name: 'Login with SSO' })).toBeVisible()
  })

  test('auth check failure: backend error redirects to login with explicit error', async ({ page }) => {
    await page.route('**/api/auth/me', async (route) => {
      await route.fulfill({
        status: 503,
        contentType: 'application/json',
        body: JSON.stringify({ title: 'Service unavailable' }),
      })
    })

    await page.goto('/admin/journey-builder')
    await expect(page).toHaveURL(/\/login\?error=auth_check_failed&returnUrl=/)
    await expect(page.getByRole('alert')).toContainText('Could not validate your admin session')
  })

  test('failed auth: invalid assertion shows access denied UI', async ({ page }) => {
    await page.goto('/login')
    const frontendOrigin = new URL(page.url()).origin

    await page.route('**/auth/saml/login**', async (route) => {
      await route.fulfill({
        status: 302,
        headers: {
          location: `${frontendOrigin}/login?error=saml_access_denied`,
        },
      })
    })

    await page.getByRole('button', { name: 'Login with SSO' }).click()
    await expect(page).toHaveURL(/\/login\?error=saml_access_denied/)
    await expect(page.getByRole('alert')).toContainText('Access Denied')
  })
})
