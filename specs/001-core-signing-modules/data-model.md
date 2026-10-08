# Data Model: Core Signing Modules

**Feature**: [spec.md](./spec.md) | **Date**: 2026-10-08

All types live in the `KeeDroidSign.Core` assembly and are independent of KeePass and WinForms.
Types marked 🔒 hold secrets: their `ToString()` redacts secret fields, and they are never logged.

## PasswordPolicy

| Field | Type | Default | Validation |
|-------|------|---------|------------|
| Length | int | 32 | 8 ≤ Length ≤ 128 |
| Uppercase | bool | true | |
| Lowercase | bool | true | |
| Digits | bool | true | |
| Symbols | bool | true | symbols are always from `!#%+,-./:=?@^_~` |
| ExcludeAmbiguous | bool | false | removes `0 O 1 l I` |

Rules: at least one class enabled; `Length ≥ number of enabled classes`; `$`, backtick, `\`, `"`,
`'`, whitespace can never be produced (FR-004). Violations → `ArgumentException` with a
descriptive message.

## DistinguishedName

| Field | Type | Required | Validation |
|-------|------|----------|------------|
| CommonName (CN) | string | yes | non-empty, ≤ 64 chars |
| OrganizationalUnit (OU) | string | no | ≤ 64 chars |
| Organization (O) | string | no | ≤ 64 chars |
| Locality (L) | string | no | ≤ 128 chars |
| State (ST) | string | no | ≤ 128 chars |
| Country (C) | string | no | exactly 2 ASCII letters, upper-cased |

Produces an RFC 4514-escaped string, e.g. `CN=Jane Doe, O=Acme\, Inc., C=UA`.

## KeyRequest 🔒

| Field | Type | Default | Validation |
|-------|------|---------|------------|
| Alias | string | — | 1–64 chars, `[A-Za-z0-9._-]`, stored lower-case by JKS |
| Subject | DistinguishedName | — | required |
| KeySize | int | 4096 | 2048, 3072 or 4096 (RSA) |
| ValidityYears | int | 30 | 25 ≤ years ≤ 100 (Android requires validity beyond 2033-10-22) |
| StorePassword | string 🔒 | — | ≥ 6 chars, no control characters |
| KeyPassword | string 🔒 | — | ≥ 6 chars, no control characters |

## SigningKeystore 🔒

| Field | Type | Notes |
|-------|------|-------|
| Content | byte[] 🔒 | raw JKS bytes; the unit stored in KeePass as an attachment |
| Alias | string | |
| StorePassword | string 🔒 | |
| KeyPassword | string 🔒 | |
| CertificateDer | byte[] (DER) | public certificate |
| NotBefore / NotAfter | DateTime (UTC) | from certificate |

Derived: `ToBase64()` → single-line standard Base64 of `Content` (FR-023).

## CertificateFingerprint

| Field | Type | Notes |
|-------|------|-------|
| Algorithm | enum `Sha256` \| `Sha1` | |
| Bytes | byte[] | 32 or 20 bytes |
| Formatted | string | `AA:BB:...` uppercase, `:`-separated (FR-014) |

## GitHubCredential 🔒

| Field | Type | Notes |
|-------|------|-------|
| Token | string 🔒 | personal access token; stored by the caller in KeePass only |

## GitHubIdentity

| Field | Type | Notes |
|-------|------|-------|
| Login | string | authenticated account login |
| TokenScopes | string[] | from `X-OAuth-Scopes` (classic PAT only; empty for fine-grained) |

## TokenValidationResult

`Status` (TokenStatus), `Identity` (GitHubIdentity, null unless Valid), `Message` (English text,
never contains the token).

## TokenStatus (enum)

`Valid` · `InvalidOrExpired` · `InsufficientPermissions` · `NetworkError` · `RateLimited`

## RepositoryTarget

| Field | Type | Validation |
|-------|------|------------|
| Owner | string | 1–39 chars, `[A-Za-z0-9-]` |
| Name | string | 1–100 chars, `[A-Za-z0-9._-]`; a trailing `.git` is stripped |

Parse helper accepts `owner/name` and `https://github.com/owner/name(.git)`.

## RepositoryAccess (enum)

`CanManageSecrets` · `NotFoundOrNoAccess` · `InsufficientPermissions` · `Archived` · `NetworkError` · `RateLimited`

## SecretMapping

| Field | Type | Default |
|-------|------|---------|
| KeystoreBase64Name | string | `ANDROID_KEYSTORE_BASE64` |
| StorePasswordName | string | `ANDROID_KEYSTORE_PASSWORD` |
| KeyAliasName | string | `ANDROID_KEY_ALIAS` |
| KeyPasswordName | string | `ANDROID_KEY_PASSWORD` |

Validation (FR-024): `^[A-Za-z_][A-Za-z0-9_]*$`, must not start with `GITHUB_` (case-insensitive),
names must be unique (case-insensitive; GitHub upper-cases names).

## SecretWrite 🔒 (internal)

`Name` + plaintext `Value`; built from `SigningKeystore` + `SecretMapping`. Values > 48 KB are
rejected before any upload.

## ExportPlan

| Field | Type | Notes |
|-------|------|-------|
| ToCreate | string[] | names not present in the repository |
| ToOverwrite | string[] | names already present (FR-021) |

## ExportResult

`Succeeded` (all Created/Updated) plus a list of `SecretOutcome { Name, Status, Reason }` where `Status` ∈
`Created` · `Updated` · `Failed` · `SkippedConflict`. `Reason` never contains secret values.

### Export state transitions

```text
Plan ──(ToOverwrite empty or overwrite=true)──> Uploading ──> Completed (per-secret outcomes)
  └──(ToOverwrite non-empty and overwrite=false)──> Refused (all SkippedConflict, nothing written)
```
