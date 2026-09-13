/**
 * Seeded demo journeys and personas.
 *
 * Operator-facing catalogue: the console lists these so an operator can pick a journey to author
 * or preview. The applicant page does not choose from them - it renders the journey its link names.
 */
export type JourneyOption = {
  id: string
  label: string
  description: string
}

export type PersonaOption = {
  key: string
  label: string
}

export const JOURNEYS: JourneyOption[] = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    label: 'Journey 1 — Small business onboarding',
    description:
      'Simple flow collecting business name, address, revenue, owner details and mocked online document verification.',
  },
  {
    id: '22222222-2222-2222-2222-222222222222',
    label: 'Journey 2 — Medium business onboarding',
    description:
      'Medium flow with primary and secondary owners, outlets/staff size, document upload, and mocked Experian + Companies House checks.',
  },
  {
    id: '33333333-3333-3333-3333-333333333333',
    label: 'Journey 3 — Large nationwide business onboarding',
    description:
      'High-complexity flow with legal structure, advanced compliance questionnaire, outlets/staff size, and mocked Experian + Companies House checks.',
  },
  {
    id: 'a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1',
    label: 'Journey A — Linear basic (test journey)',
    description:
      'Two-step linear journey: contact details form followed by a confirmation screen. Used for Playwright E2E test verification.',
  },
  {
    id: 'b2b2b2b2-b2b2-b2b2-b2b2-b2b2b2b2b2b2',
    label: 'Journey B — Conditional branch (test journey)',
    description:
      'Demonstrates conditional routing: EU applicants (France, Germany) see a GDPR disclosure; others see global terms.',
  },
  {
    id: 'c3c3c3c3-c3c3-c3c3-c3c3-c3c3c3c3c3c3',
    label: 'Journey C — Compliance heavy (test journey)',
    description:
      'Strict compliance rules, national ID pattern validation, document upload, and redirect to external verification service.',
  },
]
export const defaultFlowId = JOURNEYS[0].id
export const PERSONAS: PersonaOption[] = [
  { key: 'new-user', label: 'New User' },
  { key: 'enterprise-admin', label: 'Enterprise Admin' },
  { key: 'legacy-migratee', label: 'Legacy Migratee' },
]

