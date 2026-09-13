import { describe, expect, it } from 'vitest'

// Temporary: proves the frontend-ci vitest step fails the build. Branch is deleted after.
describe('ci gate demonstration', () => {
  it('fails on purpose', () => {
    expect(1).toBe(2)
  })
})
