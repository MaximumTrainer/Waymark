import { describe, expect, it } from 'vitest'
import config from '../vite.config'

describe('vite dev proxy', () => {
  it('proxies auth routes to backend', () => {
    const proxy = (config as { server?: { proxy?: Record<string, { target: string; changeOrigin: boolean }> } }).server?.proxy
    expect(proxy).toBeDefined()
    expect(proxy).toHaveProperty('/auth')
    expect(proxy?.['/auth']).toMatchObject({
      target: 'http://localhost:5072',
      changeOrigin: true,
    })
  })
})
