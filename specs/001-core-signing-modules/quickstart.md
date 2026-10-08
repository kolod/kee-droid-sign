# Quickstart: Validating the Core Signing Modules

**Feature**: [spec.md](./spec.md) | **API**: [contracts/core-api.md](./contracts/core-api.md)

## Prerequisites

- Windows with .NET Framework 4.8 runtime and a .NET SDK (8.0 or newer) for `dotnet build/test`.
- Optional: a JDK 11+ (`keytool`) for the single interoperability test. Located via
  `KDS_KEYTOOL`, then `JAVA_HOME\bin\keytool.exe`, then `PATH`. Without it that test is
  **skipped**, not failed. Nothing else needs Java.
- No network access or GitHub account is needed for the test suite.

## 1. Build and run all tests

```powershell
dotnet build KeeDroidSign.sln -c Release
dotnet test  KeeDroidSign.sln -c Release
```

Expected: all tests pass; if no JDK is found, `Keystore/KeytoolInteropTests` shows as skipped
with reason "keytool not found".

## 2. Scenario checks (map to spec user stories)

| Story | Test class (tests/KeeDroidSign.Core.Tests) | What it proves |
|-------|---------------------------------------------|----------------|
| US1 | `Passwords/PasswordGeneratorTests` | length, class coverage, 10,000-sample forbidden-character check, unsatisfiable rules rejected |
| US2 | `Keystore/KeystoreGeneratorTests` | `.jks` reopens with store/key passwords, alias, RSA 4096, subject, validity ≥ requested; no file written without an output path; existing output file not overwritten; cancellation |
| US2 | `Keystore/DistinguishedNameTests` | subject fields with `, + " = ;` and non-ASCII letters round-trip exactly; country code validation |
| US2 | `Keystore/KeytoolInteropTests` (optional) | JDK `keytool -list` opens the generated keystore with both passwords |
| US3 | `Fingerprints/CertificateFingerprintTests` | SHA-256 and SHA-1 of a fixture certificate equal reference values; format `AA:BB:...` |
| US4 | `GitHub/GitHubClientTests` | `/user` → login; 401 → InvalidOrExpired; rate limit; repo access states; token absent from all messages |
| US5 | `GitHub/SecretExporterTests`, `GitHub/SealedBoxTests` | create vs update, conflict refusal without `overwrite`, custom names, per-secret failures; sealed box matches libsodium |
| US6 | whole suite | runs offline; secret-leak scan over captured messages (SC-007) |

Run one area: `dotnet test --filter "FullyQualifiedName~GitHub"`.

## 3. Manual cross-check with the JDK (optional)

After running `KeytoolInteropTests` (or any keystore test) with `KDS_KEEP_TEST_OUTPUT=1`, the test writes
`artifacts/test-output/sample.jks` (test-only passwords) and prints its fingerprint:

```powershell
keytool -list -v -keystore artifacts/test-output/sample.jks -storepass test-store-pass
```

Expected: `SHA256:` line in keytool output equals the fingerprint printed by the test.

## 4. Manual cross-check of the CI contract (optional)

Decode the Base64 produced by `SigningKeystore.ToBase64()` with GNU `base64 -d` (e.g. Git Bash)
and confirm the output is byte-identical to the keystore — this is the exact step used by the
reference GitHub Actions workflow in the spec.
