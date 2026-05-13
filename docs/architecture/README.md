# Architecture Overview

This directory contains C4 architecture diagrams for the Waymark (open-onboarding) system.

## Diagrams

| File | C4 Level | Description |
|------|----------|-------------|
| [context.md](context.md) | Level 1 — System Context | Waymark and the people/systems around it |
| [containers.md](containers.md) | Level 2 — Containers | Major deployable units (API, SPA, DB, etc.) |
| [components.md](components.md) | Level 3 — Components | Internal structure of the API |

## Viewing Diagrams

All diagrams use [Mermaid C4 syntax](https://mermaid.js.org/syntax/c4.html).

- **GitHub**: renders automatically in `.md` files
- **VS Code**: install the [Mermaid Preview](https://marketplace.visualstudio.com/items?itemName=bierner.markdown-mermaid) extension
- **CLI**: `npx @mermaid-js/mermaid-cli@latest -i context.md -o context.svg`

## Architecture Principles

### Ports & Adapters (Hexagonal Architecture)

The Waymark API uses a strict ports & adapters structure:

```
┌────────────────────────────────────────┐
│  Adapters (Infrastructure Layer)       │
│  - LocalDocumentStorageService         │
│  - InMemorySessionEventEmitter         │
│  - OnboardingDbContext (EF Core)       │
│  - WebhookHttpClient                   │
├────────────────────────────────────────┤
│  Application Layer (Ports / Use Cases) │
│  - WorkflowService                     │
│  - FlowService, CustomerService        │
│  - WebhookService                      │
│  - IDocumentStorageService (port)      │
│  - ISessionEventEmitter (port)         │
│  - IWebhookHttpClient (port)           │
├────────────────────────────────────────┤
│  Domain Layer (Pure Business Logic)    │
│  - ComplianceRuleEvaluator             │
│  - LogicNodeExecutor chain             │
│  - Entities: Flow, Node, Session, etc. │
└────────────────────────────────────────┘
```

**Dependency rule**: inner layers never depend on outer layers.
Controllers and infrastructure adapters depend on application interfaces (ports), not the other way around.

### Data Flow: Step Submission

```
POST /sessions/{id}/steps/{nodeId}/submit
    ↓
WorkflowController.SubmitStep()
    ↓
WorkflowService.SubmitStepAsync()
    ↓ validate request (FluentValidation)
    ↓ load session + current node from DB
    ↓ evaluate ComplianceRules → if violations → ComplianceViolationException (422)
    ↓ persist Submission
    ↓ resolve next node via NodeConnections
    ↓ if logic node: ILogicNodeExecutor.ExecuteAsync() (may auto-advance multiple nodes)
    ↓ if end node: mark session Complete; dispatch webhook
    ↓ emit SSE event via ISessionEventEmitter
    ↓ return 200 + SubmitStepResponse
```
