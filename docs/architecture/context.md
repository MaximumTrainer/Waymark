# System Context Diagram (C4 Level 1)

This diagram shows Waymark (open-onboarding) and how it relates to the people and systems around it.

```mermaid
C4Context
    title Waymark — System Context

    Person(operator, "Operator", "Configures onboarding flows, monitors sessions, inspects webhook deliveries")
    Person(applicant, "Applicant", "Completes an onboarding journey (e.g., company registration, KYC)")
    Person(developer, "Developer", "Integrates Waymark into their product via webhook callbacks")

    System(waymark, "Waymark", "Schema-driven onboarding platform — hosts flows, evaluates compliance rules, dispatches webhooks, streams progress via SSE")

    System_Ext(idp, "Identity Provider (OIDC/JWT)", "Issues JWT tokens for authenticated access (e.g., Auth0, Keycloak, Azure AD B2C)")
    System_Ext(webhook_consumer, "Webhook Consumer", "Developer's backend system that receives session lifecycle events (e.g., session-completed, step-advanced)")
    System_Ext(virus_scanner, "Virus Scanner (ClamAV / VirusTotal)", "Scans uploaded documents for malware before they are stored")

    Rel(operator, waymark, "Manages flows, nodes, connections, webhooks", "HTTPS / REST API")
    Rel(applicant, waymark, "Completes onboarding steps, uploads documents", "HTTPS / REST API + SSE")
    Rel(developer, waymark, "Registers webhooks, queries analytics", "HTTPS / REST API")

    Rel(waymark, idp, "Validates JWT Bearer tokens", "HTTPS / OIDC")
    Rel(waymark, webhook_consumer, "Dispatches signed webhook events", "HTTPS POST + HMAC-SHA256 signature")
    Rel(waymark, virus_scanner, "Scans uploaded files before storage", "Internal / HTTP API")
```

## Key Interactions

| Actor | Interaction | Protocol |
|-------|-------------|----------|
| Operator | Create/edit flows, view analytics, inspect webhook deliveries | REST API (JWT or API Key) |
| Applicant | Start session, submit steps, upload documents, receive live updates | REST API + Server-Sent Events |
| Developer | Register webhooks, receive callbacks, query session history | REST API + HTTPS webhooks |

## Authentication

- **Operators** authenticate via JWT Bearer (from OIDC provider) or X-Api-Key header
- **Applicants** authenticate via JWT Bearer (scoped to Applicant role) or X-Api-Key
- JWT authority is required in non-Development environments (`Authentication:JwtAuthority`)
