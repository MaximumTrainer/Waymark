import { describe, expect, it } from 'vitest'
import { resolveFlowIdForPersona, upsertPersonaAssignment, type PersonaAssignment } from './personaRouting'

describe('persona routing', () => {
  it('uses persona-mapped flow when available', () => {
    const assignments: PersonaAssignment[] = [
      { personaKey: 'enterprise-admin', flowId: 'flow-enterprise', liveVersion: 7 },
    ]

    expect(resolveFlowIdForPersona(assignments, 'enterprise-admin', 'flow-default')).toBe('flow-enterprise')
  })

  it('falls back to default flow when persona has no mapping', () => {
    const assignments: PersonaAssignment[] = [
      { personaKey: 'new-user', flowId: 'flow-new-user', liveVersion: 2 },
    ]

    expect(resolveFlowIdForPersona(assignments, 'legacy-migratee', 'flow-default')).toBe('flow-default')
  })

  it('replaces existing assignment for the same persona key', () => {
    const assignments: PersonaAssignment[] = [
      { personaKey: 'new-user', flowId: 'flow-a', liveVersion: 1 },
    ]

    const updated = upsertPersonaAssignment(assignments, {
      personaKey: 'new-user',
      flowId: 'flow-b',
      liveVersion: 3,
    })

    expect(updated).toEqual([{ personaKey: 'new-user', flowId: 'flow-b', liveVersion: 3 }])
  })
})
