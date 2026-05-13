import { configDefaults, defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    environment: 'node',
    globals: true,
    testTimeout: 30000,
    exclude: [...configDefaults.exclude, 'playwright/**'],
  },
})
