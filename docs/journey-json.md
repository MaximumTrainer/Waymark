# journey.json reference

This document explains how a `journey.json` (Waymark flow definition) is structured, how to read its notation, how it maps to frontend components, and how to visualize journey paths.

## What `journey.json` represents

`journey.json` is the data model for a Waymark journey. In API terms, it is a **flow definition** with:

- top-level flow metadata
- `nodes` (steps)
- `connections` (directed edges between steps)

Waymark evaluates this graph at runtime to decide the next step for each session.

## Notation and conventions

- IDs are GUID strings (for `flow`, `node`, and connection references).
- `nodes[].jsonContent` is itself JSON, stored as a **string** in the API contract.
- `nodes[].complianceRuleJson` is optional JSON, also stored as a **string**.
- `connections[].priority` is an integer; lower values are evaluated first.
- `condition*` fields can be omitted/null for fallback (unconditional) edges.

### Minimal shape

```json
{
  "name": "Journey name",
  "description": "Optional description",
  "nodes": [],
  "connections": []
}
```

### Example with escaped node schemas

```json
{
  "name": "SMB onboarding",
  "description": "Collect business profile and route by country",
  "nodes": [
    {
      "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "key": "country-form",
      "type": "Form",
      "title": "Tell us about your business",
      "jsonContent": "{\"fields\":[{\"name\":\"CompanyName\",\"type\":\"text\",\"required\":true},{\"name\":\"Country\",\"type\":\"select\",\"required\":true,\"options\":[\"USA\",\"GBR\"]}]}",
      "complianceRuleJson": "{\"requiredFields\":[\"CompanyName\",\"Country\"]}",
      "isStartNode": true
    },
    {
      "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "key": "passport-upload",
      "type": "DocumentUpload",
      "title": "Upload supporting documents",
      "jsonContent": "{\"acceptedFileTypes\":[\"application/pdf\",\"image/jpeg\"],\"maxFiles\":2}",
      "isStartNode": false
    }
  ],
  "connections": [
    {
      "sourceNodeId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "targetNodeId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "conditionField": "Country",
      "conditionOperator": "NotEquals",
      "conditionValue": "USA",
      "priority": 0
    }
  ]
}
```

## Structure reference

### Create/update payload fields (`POST /api/flows`, `PUT /api/flows/{flowId}`)

| Field | Required | Description |
|---|---|---|
| `name` | Yes | Human-readable journey name. |
| `description` | No | Optional journey description. |
| `nodes` | Yes | Array of step definitions. |
| `connections` | Yes | Array of directed edges. |

### Node fields

| Field | Required | Description |
|---|---|---|
| `id` | No | GUID for node identity and edge linkage (defaults to a generated GUID when omitted). |
| `key` | Yes | Stable logical identifier (slug-like). |
| `type` | Yes | `Form`, `DocumentUpload`, `Redirect`, `Information`, or `Logic`. |
| `title` | Yes | Label/instruction rendered in the UI. |
| `jsonContent` | No | Type-specific JSON serialized as a string (defaults to the JSON string `"{}"` when omitted). |
| `complianceRuleJson` | No | Optional validation rules JSON (string). |
| `isStartNode` | Yes | Exactly one node in a journey should be `true`. |

### Connection fields

| Field | Required | Description |
|---|---|---|
| `sourceNodeId` | Yes | Origin node GUID. |
| `targetNodeId` | Yes | Destination node GUID. |
| `conditionField` | No | Payload/profile field to evaluate. |
| `conditionOperator` | No | Comparison operator (`Equals`, `GreaterThan`, etc.). |
| `conditionValue` | No | Value used by the operator. |
| `priority` | Yes | Evaluation order (ascending). |

### Read model fields returned by the API (`GET /api/flows/{flowId}`)

The API response includes additional identity/version fields that are not part of the write payload:

- Top-level flow: `id`, `version`
- Node objects: `id`, `flowId`
- Connection objects: `id`, `flowId`

## Frontend linkage

The journey JSON links directly to frontend behavior:

| Journey JSON field | Frontend component/hook | Effect |
|---|---|---|
| `id` (selected flow) | `App.tsx` + `useOnboarding` | Passed as `StartSessionRequest.flowId` to start a session for the selected journey. |
| `nodes[].type` | `StepRenderer.tsx` | Chooses which UI is rendered for the current step. |
| `nodes[].jsonContent` | `StepRenderer.tsx` | Parsed to build dynamic form fields, upload config, redirect URL, etc. |
| `nodes[].complianceRuleJson` | API + `ComplianceError` handling in `StepRenderer.tsx` | Server validates payload and returns field/global errors. |
| `connections[]` | Runtime engine + `JourneyBuilder.tsx` | Drives routing and shows labeled graph edges in the read-only session monitor. |
| `nodes[]`/`connections[]` | `VisualJourneyBuilder.tsx` + `VisualJourneyCanvas.tsx` | Edited visually via drag-and-drop in the admin builder at `/admin/journey-builder`. |
| `nodes[]`/`connections[]` | `FlowAuthoringPanel.tsx` + `flowAuthoring.ts` | Alternative JSON authoring panel for power users; runs alongside the visual builder. |
| `nodes[].type` | `VisualJourneyCanvas.tsx` (`NODE_TYPE_STYLES`) | Determines the color scheme of each node on the canvas (Form=blue, DocumentUpload=purple, Redirect=amber, Information=green, Logic=orange). |
| `nodes[].isStartNode` | `VisualJourneyCanvas.tsx` + `NodePropertiesPanel.tsx` | Start node receives a bold outline; Properties Panel enforces at most one start node. |

## Journey visualization

Waymark provides three complementary views:

1. **Runtime step UI** via `StepRenderer` — renders the current node's content to the end user.
2. **Session graph monitor** via `JourneyBuilder` — read-only React Flow diagram; highlights the current and visited nodes during an active session.
3. **Interactive admin canvas** via `VisualJourneyCanvas` inside `VisualJourneyBuilder` — the drag-and-drop authoring surface at `/admin/journey-builder` where operators create and edit flows.

Conceptually, a journey is a directed graph:

```mermaid
flowchart LR
  A[Start: country-form] -->|Country = USA| B[us-tax-form]
  A -->|"Country != USA"| C[passport-upload]
  B --> D[completed]
  C --> D
```

## Authoring tip

The easiest way to create a flow is via the **Visual Journey Builder** at `/admin/journey-builder` (Operator SSO required). You can drag nodes onto the canvas, draw connections between them, and edit every property through the side panel — no JSON required. Saving from the builder posts the serialized draft to the API automatically.

For scripted or batch scenarios, the underlying JSON schema is documented above. Canonical examples in this repository:

- [`flow-definition.example.json`](../src/frontend/src/schemas/flow-definition.example.json)
- [`journey-builder.schema.json`](../src/frontend/src/schemas/journey-builder.schema.json) as the builder/renderer source-of-truth contract for nodes, edges, lifecycle state, and persona assignments.

When posting through the API directly, ensure each node's `jsonContent` and `complianceRuleJson` are valid JSON strings.
