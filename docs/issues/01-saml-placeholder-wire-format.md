---
title: "Replace placeholder SAML wire format with standard SAML XML"
labels: ["bug", "security", "authentication"]
---

## Summary

The current SAML authentication controller (`SamlAuthController`) uses a **temporary placeholder wire format** that serialises the authentication request and assertion as Base64-encoded JSON instead of the standard SAML 2.0 XML envelope. This means real SAML identity providers (Azure AD, Okta, Auth0, etc.) cannot be connected without replacing this implementation.

The placeholder is gated behind `Authentication:Saml:EnablePlaceholderProvider` and all three endpoints (`GET /auth/saml/metadata`, `GET /auth/saml/login`, `POST /auth/saml/callback`) return `404 Not Found` when the flag is `false`.

**Affected file:** `src/backend/OpenOnboarding.Api/Controllers/SamlAuthController.cs`

### Current placeholder behaviour (lines 69–79)

```csharp
// Placeholder wire format:
// Until full SAML XML tooling is integrated, we encode a compact JSON envelope that
// tests and local mocks can round-trip deterministically.
var samlRequestPayload = new
{
    issuer = configuration["Authentication:Saml:Issuer"] ?? "waymark-service-provider",
    assertionConsumerServiceUrl = ResolveAcsUrl()
};
var samlRequest = Convert.ToBase64String(
    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(samlRequestPayload)));
```

And on callback (lines 279–293):

```csharp
// Placeholder wire format:
// IdP assertions are expected to be base64-encoded JSON in this temporary implementation.
var jsonBytes = Convert.FromBase64String(encodedResponse);
return JsonSerializer.Deserialize<SamlAssertionPayload>(jsonBytes, AssertionJsonOptions);
```

---

## Requirements

### 1 — AuthnRequest generation (`GET /auth/saml/login`)

- Produce a standard `<samlp:AuthnRequest>` XML document signed with the SP private key.
- Base64-encode and optionally deflate-compress the XML per the **HTTP-Redirect binding** (SAML 2.0 §3.4).
- Include required attributes: `ID`, `Version="2.0"`, `IssueInstant`, `Destination`, `AssertionConsumerServiceURL`, `<saml:Issuer>`.
- Remove the JSON fallback path entirely.

### 2 — Assertion Consumer Service (`POST /auth/saml/callback`)

- Parse the `SAMLResponse` form field as a Base64-encoded XML document.
- Validate the XML digital signature of the `<samlp:Response>` and/or `<saml:Assertion>` using the configured IdP certificate.
- Verify: `InResponseTo` matches the issued `AuthnRequest` ID; `NotBefore`/`NotOnOrAfter` conditions; `Recipient` ACS URL matches.
- Support both signed responses and signed assertions.
- Extract `NameID`, email, and display-name attributes from the assertion.
- Return 400 / redirect to `/login?error=saml_invalid_assertion` on any verification failure (existing error-redirect helper can be reused).

### 3 — SP Metadata (`GET /auth/saml/metadata`)

- Expose the SP X.509 signing certificate in the `<KeyDescriptor use="signing">` element so IdPs can import it automatically.
- Make the entity ID (`Authentication:Saml:Issuer`) and ACS URL consistent with the login endpoint.

### 4 — Configuration

| Key | Required | Notes |
|-----|----------|-------|
| `Authentication:Saml:Issuer` | Yes | SP entity ID |
| `Authentication:Saml:IdpSsoUrl` | Yes | IdP SSO endpoint |
| `Authentication:Saml:IdpCertificate` | Yes | PEM or Base64 DER of IdP signing cert |
| `Authentication:Saml:SpCertificate` | Yes | PEM of SP signing cert |
| `Authentication:Saml:SpPrivateKey` | Yes | PEM of SP private key (stored as secret) |
| `Authentication:Saml:EnablePlaceholderProvider` | No | Remove flag once real implementation ships |

### 5 — Dependency

Prefer a well-maintained .NET SAML library (e.g. `ITfoxtec.Identity.Saml2`) rather than hand-rolling XML signature logic.  Validate any chosen library version against the GitHub Advisory Database before adding it.

---

## Acceptance Criteria

- [ ] `GET /auth/saml/login` redirects to the IdP with a valid, standards-compliant `SAMLRequest` query parameter (Base64-deflated XML, not JSON).
- [ ] `POST /auth/saml/callback` accepts a real IdP `SAMLResponse`, verifies the XML signature, and issues an `AdminSession` cookie on success.
- [ ] An expired, tampered, or missing signature causes a redirect to `/login?error=saml_invalid_assertion`.
- [ ] An `InResponseTo` mismatch causes a redirect to `/login?error=saml_invalid_assertion`.
- [ ] A `NotOnOrAfter` violation causes a redirect to `/login?error=saml_invalid_assertion`.
- [ ] `GET /auth/saml/metadata` returns well-formed SAML metadata XML that includes the SP signing certificate.
- [ ] `Authentication:Saml:EnablePlaceholderProvider` flag and all placeholder code paths are removed.
- [ ] All existing `SamlAuthControllerTests` tests are updated or replaced to cover the real implementation; placeholder-specific tests are removed.
- [ ] A new test exercises the full login → callback → session flow using a self-signed test certificate.
