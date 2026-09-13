import { configDefaults, defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test-setup.ts'],
    testTimeout: 30000,
    // Vitest owns src/**; Playwright owns e2e/** (see playwright.config.ts testDir).
    // Collecting the Playwright specs here makes them fail on test.describe().
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    exclude: [...configDefaults.exclude, 'e2e/**'],
  },
})
