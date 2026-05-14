export interface PersonaAssignment {
  personaKey: string
  flowId: string
  liveVersion: number | null
}

export function resolveFlowIdForPersona(
  assignments: readonly PersonaAssignment[],
  personaKey: string,
  fallbackFlowId: string,
): string {
  return assignments.find((assignment) => assignment.personaKey === personaKey)?.flowId ?? fallbackFlowId
}

export function upsertPersonaAssignment(
  assignments: readonly PersonaAssignment[],
  nextAssignment: PersonaAssignment,
): PersonaAssignment[] {
  const withoutPersona = assignments.filter((assignment) => assignment.personaKey !== nextAssignment.personaKey)
  return [...withoutPersona, nextAssignment]
}

export function buildVersionToPersonaMap(assignments: readonly PersonaAssignment[]): Record<number, string[]> {
  return assignments.reduce<Record<number, string[]>>((acc, assignment) => {
    if (assignment.liveVersion === null) return acc
    const existing = acc[assignment.liveVersion] ?? []
    acc[assignment.liveVersion] = [...existing, assignment.personaKey]
    return acc
  }, {})
}
