---
title: "SAML metadata advertises assertion encryption the ACS cannot decrypt"
labels: ["bug", "security", "authentication"]
---

## Summary

`GET /auth/saml/metadata` publishes a `<KeyDescriptor use="encryption">` containing the SP certificate, telling every identity provider that Waymark accepts encrypted assertions. The callback never configures a decryption certificate, so an IdP that honours the advertised key will produce `EncryptedAssertion` responses that the ACS cannot read — every login fails with a generic `saml_invalid_assertion` redirect.

Azure AD, Okta and Auth0 all offer to encrypt assertions when the SP metadata advertises an encryption key, and some tenants enforce it by policy.

**Affected file:** `src/backend/OpenOnboarding.Api/Controllers/SamlAuthController.cs`

### Metadata advertises the capability (line 45)

```xml
<KeyDescriptor use="encryption">
  <ds:KeyInfo xmlns:ds="http://www.w3.org/2000/09/xmldsig#">
    <ds:X509Data>
      <ds:X509Certificate>{certBase64}</ds:X509Certificate>
    </ds:X509Data>
  </ds:KeyInfo>
</KeyDescriptor>
```

### `BuildSamlConfiguration` never sets a decryption certificate

```csharp
var config = new Saml2Configuration
{
    Issuer = issuer,
    SingleSignOnDestination = new Uri(idpSsoUrl),
    SigningCertificate = spCert,
    CertificateValidationMode = X509CertificateValidationMode.None,
    RevocationMode = X509RevocationMode.NoCheck
};
```

`Saml2Configuration.DecryptionCertificate` is required for ITfoxtec to unwrap an `EncryptedAssertion`. The SP certificate is already loaded with its private key (`X509Certificate2.CreateFromPem(certPem, keyPem)`), so the material needed is in hand.

### Related loose end

`src/frontend/src/App.tsx` defines login error messages for `saml_assertion_not_encrypted` and `saml_certificate_expired`, but the controller emits only `saml_invalid_assertion`, `saml_access_denied` and `saml_csrf_failed`. Those two messages are unreachable, and an encrypted-assertion failure surfaces to the operator as the generic "Invalid SAML assertion" with nothing in the logs distinguishing it.

---

## Requirements

1. Set `DecryptionCertificate` on the `Saml2Configuration` from the configured SP certificate and private key so `EncryptedAssertion` responses decrypt.
2. Add an optional `Authentication:Saml:RequireEncryptedAssertion` setting. When `true`, reject an unencrypted assertion and redirect with `saml_assertion_not_encrypted`.
3. If encryption will not be supported, remove the `use="encryption"` `KeyDescriptor` from metadata instead — the current state advertises a capability that does not exist and must not stay as-is.
4. Distinguish decryption and signature failures in the logs so an operator can tell them apart from the generic redirect.
5. Reconcile the frontend error-code table with the codes the controller actually emits (including `saml_certificate_expired`, which is currently unreachable).

---

## Acceptance Criteria

- [ ] A `POST /auth/saml/callback` carrying an `EncryptedAssertion` encrypted to the SP certificate decrypts successfully and issues an `AdminSession` cookie.
- [ ] Both a signed unencrypted assertion and a signed encrypted assertion are accepted when `RequireEncryptedAssertion` is unset or `false`.
- [ ] With `RequireEncryptedAssertion=true`, an unencrypted assertion redirects to `/login?error=saml_assertion_not_encrypted`.
- [ ] An assertion encrypted to the wrong key redirects to `/login?error=saml_invalid_assertion` and logs a decryption-specific message.
- [ ] Tests cover the encrypted happy path, the wrong-key failure, and the `RequireEncryptedAssertion` rejection, using self-signed test certificates in the style of the existing `SamlAuthControllerTests`.
- [ ] Every error code in the frontend `LOGIN_ERROR_MESSAGES` table is reachable from the controller, or is removed.
- [ ] `docs/runbook.md` documents the encryption configuration and the IdP-side setup it implies.
