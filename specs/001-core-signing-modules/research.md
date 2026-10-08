# Research: Core Signing Modules

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-10-08

## R1. Target framework

- **Decision**: `net48` (.NET Framework 4.8), SDK-style project built with the installed .NET SDK
  (reference assemblies come from `Microsoft.NETFramework.ReferenceAssemblies`).
- **Rationale**: The submodule is KeePass 2.61.1, which ships `KeePass_N48.sln` and runs on CLR 4
  (`supportedRuntime v4.0` in `KeePass.exe.config`). net48 gives `async`/`await`, `HttpClient`,
  TLS 1.2+ via OS defaults, and is supported by BouncyCastle.Cryptography (net461+).
- **Alternatives considered**: net35 (matches the legacy `KeePass.csproj` but has no
  `HttpClient`, no TPL-based async, and no maintained crypto library); netstandard2.0 (would work
  for the core, but adds facade assemblies to deploy next to KeePass with no benefit).

## R2. Keystore generation

- **Decision**: Generate everything in-process with BouncyCastle: `RsaKeyPairGenerator`
  (`SecureRandom`, public exponent 65537, 4096 bits by default) → self-signed X.509 v3
  certificate via `X509V3CertificateGenerator` + `Asn1SignatureFactory("SHA256WITHRSA")`
  (random positive 128-bit serial, `NotBefore = now − 1 day`, `NotAfter = NotBefore + years`) →
  `JksStore.SetKeyEntry(alias, key, keyPassword, chain)` → `JksStore.Save(memoryStream,
  storePassword)`. The resulting bytes are returned to the caller; nothing touches the disk
  unless an explicit output path is passed (FR-010, FR-011).
- **Rationale**: No Java dependency for users, no temporary files with key material
  (Constitution III), no process launching or argument quoting, and fully deterministic offline
  tests. JKS with separate store and key passwords is supported by `JksStore`.
- **Interoperability**: an optional test runs `keytool -list -v` (JDK found via `KDS_KEYTOOL`,
  `JAVA_HOME`, `PATH`) against a generated keystore to confirm compatibility (FR-012); skipped
  when no JDK is present.
- **Alternatives considered**: Calling the JDK `keytool` (original plan) — rejected: requires a
  configured Java install, a temp file for the keystore, process management and command-line
  quoting of user-provided subject fields. .NET `RSA` + `CertificateRequest` — not available on
  net48 and cannot write JKS anyway.

## R3. Reading keystores and fingerprints

- **Decision**: Use `Org.BouncyCastle.Security.JksStore` (`Load(stream, password)`,
  `GetKey(alias, keyPassword)`, `GetCertificate(alias)`) to verify generated keystores and to read
  certificates. Fingerprint = SHA-256 (or SHA-1) over the DER-encoded certificate
  (`X509Certificate.GetEncoded()`), formatted as uppercase hex pairs joined by `:`.
- **Rationale**: Works without Java, so fingerprint and verification tests run on any machine;
  `JksStore.Load` checks the store integrity hash (wrong password → error), `GetKey` validates the
  key password.
- **Alternatives considered**: Parsing `keytool -list -v` output (locale-dependent, needs Java);
  writing a custom JKS parser (unnecessary given BouncyCastle).

## R4. GitHub secret encryption (libsodium sealed box)

- **Decision**: Implement `crypto_box_seal` in managed code on BouncyCastle primitives:
  ephemeral X25519 key pair → `nonce = BLAKE2b-192(epk ‖ recipientPk)` → shared key
  `k = HSalsa20(X25519(esk, recipientPk), 0¹⁶)` → `XSalsa20-Poly1305(k, nonce, message)` →
  output `epk ‖ tag ‖ ciphertext`, Base64-encoded. HSalsa20 is a ~30-line function over the Salsa20
  core; XSalsa20 (`XSalsa20Engine`), Poly1305, BLAKE2b and X25519 come from BouncyCastle.
- **Rationale**: GitHub requires LibSodium sealed boxes (`encrypted_value` + `key_id`). A managed
  implementation avoids shipping a native `libsodium.dll` inside a KeePass plugin.
- **Verification**: Unit tests (a) check HSalsa20/crypto_box against libsodium published test
  vectors, (b) round-trip via a test-only `crypto_box_seal_open`, and (c) cross-check with the
  `Sodium.Core` NuGet package (native libsodium) referenced **only by the test project**.
- **Alternatives considered**: `Sodium.Core` in production (native binary per architecture next
  to KeePass, harder deployment); other managed NaCl ports (less maintained, extra dependency).

## R5. GitHub REST API usage

Source: GitHub REST docs (Actions secrets), API version `2022-11-28`.

| Purpose | Request | Notes |
|---------|---------|-------|
| Validate token / get login | `GET /user` | 200 → `login`; 401 → invalid/expired |
| Repository access check | `GET /repos/{owner}/{repo}` then `GET /repos/{owner}/{repo}/actions/secrets?per_page=1` | 404 → not found or no access; 403 → insufficient permission/scope |
| List existing secrets | `GET /repos/{owner}/{repo}/actions/secrets?per_page=100&page=n` | paginate until all `total_count` read |
| Repository public key | `GET /repos/{owner}/{repo}/actions/secrets/public-key` | returns `key_id`, `key` (Base64 X25519) |
| Create/update secret | `PUT /repos/{owner}/{repo}/actions/secrets/{name}` body `{encrypted_value, key_id}` | 201 created, 204 updated |

- **Headers**: `Authorization: Bearer <token>`, `Accept: application/vnd.github+json`,
  `X-GitHub-Api-Version: 2022-11-28`, `User-Agent: KeeDroidSign/<version>`.
- **Token permissions**: fine-grained PAT with repository permission *Secrets: Read and write*
  (and *Metadata: Read*); classic PAT with `repo` scope.
- **Rate limiting**: 403/429 with `x-ratelimit-remaining: 0` or `retry-after` → reported as a
  distinct `RateLimited` failure, no automatic retry loop.
- **Write permission** cannot be proven without writing; the access check reports
  "can read secrets"; a write denial surfaces per secret in the export result.
- **HTTP**: `HttpClient` with an injectable `HttpMessageHandler` (tests use a fake handler); base
  address fixed to `https://api.github.com` (HTTPS only, FR-020).
- **JSON**: `DataContractJsonSerializer` (in the framework) — avoids extra dependencies.

## R6. Certificate subject

- **Decision**: Build the subject as an `X509Name` from ordered OID/value pairs
  (`CN, OU, O, L, ST, C`) — no string parsing, so commas, quotes and `=` in user input need no
  escaping and cannot inject extra attributes. Country code is validated as two ASCII letters;
  non-ASCII letters are encoded as UTF8String. `DistinguishedName.ToString()` produces an
  RFC 4514 display string for the UI only.
- **Alternatives considered**: Formatting a `-dname` string with RFC 4514 escaping (needed only
  for keytool; obsolete with R2).

## R7. Password generation

- **Decision**: `RNGCryptoServiceProvider`-backed generator with rejection sampling (no modulo
  bias). Classes: `A–Z`, `a–z`, `0–9`, symbols `!#%+,-./:=?@^_~` (FR-002/FR-004). One character of
  each enabled class is placed first, the rest drawn from the union, then a Fisher–Yates shuffle
  with the same RNG. Optional exclusion of ambiguous characters `0 O 1 l I`. Minimum length 8,
  maximum 128 (the JKS ecosystem expects ≥ 6).

## R8. Test framework

- **Decision**: xUnit 2.x on net48, run with `dotnet test`. GitHub tests use a fake
  `HttpMessageHandler`; no network access. The single JDK interoperability test uses a custom
  `[KeytoolFact]` attribute that skips with a reason when no JDK is found (spec US6 scenario 2).
- **Alternatives considered**: NUnit / MSTest — equivalent; xUnit chosen for its default
  parallelism and simple custom skip attributes.

## R9. Secret handling inside the core

- **Decision**: Passwords and tokens are passed as `string` (KeePass `ProtectedString.ReadString()`
  yields strings anyway); keystore content as `byte[]`. Types holding secrets override `ToString()`
  to redact values; exceptions are built from fixed messages plus non-secret context only.
  A test scans all captured log/exception text for the secret values used (SC-007).
