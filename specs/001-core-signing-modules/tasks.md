# Tasks: Core Signing Modules

**Input**: Design documents from `/specs/001-core-signing-modules/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/core-api.md](./contracts/core-api.md),
[contracts/github-http.md](./contracts/github-http.md), [quickstart.md](./quickstart.md)

**Tests**: REQUIRED — explicitly requested in the spec (User Story 6) and by Constitution
Principle VI. Within each story, write tests first and confirm they fail before implementing.

**Organization**: Tasks are grouped by user story so each story can be implemented and verified
independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User story the task belongs to (US1–US6)

## Path Conventions

- Production code: `src/KeeDroidSign.Core/`
- Tests: `tests/KeeDroidSign.Core.Tests/`
- All code, comments and identifiers in English (Constitution Principle I)
- Namespaces follow folders: `KeeDroidSign.Core.Passwords`, `.Keystore`, `.Fingerprints`, `.GitHub`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Solution, projects and build configuration

- [X] T001 Create `Directory.Build.props` at repo root: `TargetFramework` net48, `LangVersion` 12, `Nullable` disable, `TreatWarningsAsErrors` true, `Deterministic` true, `Company`/`Product` = KeeDroidSign, `Version` 0.1.0
- [X] T002 Create class library project `src/KeeDroidSign.Core/KeeDroidSign.Core.csproj` (SDK-style, net48, references `System.Net.Http` and `System.Runtime.Serialization` framework assemblies, NuGet `BouncyCastle.Cryptography` 2.7.x, `InternalsVisibleTo` KeeDroidSign.Core.Tests)
- [X] T003 Create test project `tests/KeeDroidSign.Core.Tests/KeeDroidSign.Core.Tests.csproj` (net48, `xunit` 2.9.x, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, test-only `Sodium.Core`, project reference to KeeDroidSign.Core; `PlatformTarget` x64 so native libsodium loads)
- [X] T004 Create `KeeDroidSign.sln` at repo root containing both projects (`dotnet new sln` + `dotnet sln add`); do NOT add any project from `keepass/`
- [X] T005 [P] Create `.gitignore` at repo root (bin/, obj/, .vs/, *.user, TestResults/, artifacts/) and `.editorconfig` (tabs/spaces per C# defaults, UTF-8, LF for *.md)
- [X] T006 Verify `dotnet build KeeDroidSign.sln -c Release` succeeds with empty projects

**Checkpoint**: Solution builds; test project discovers zero tests

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared exception hierarchy and test support used by every story

- [X] T007 Create exception hierarchy in `src/KeeDroidSign.Core/KeeDroidSignException.cs`: base class `KeeDroidSignException : Exception` plus sealed `InvalidKeystorePasswordException`, `InvalidKeyPasswordException`, `AliasNotFoundException`, `KeystoreExistsException`, `GitHubApiException` — constructors accept only fixed English messages and non-secret context (never passwords, tokens or key bytes)
- [X] T008 [P] Create `src/KeeDroidSign.Core/Redaction.cs` with `internal static class Redaction { public const string Placeholder = "***"; }` used by every `ToString()` override of secret-holding types
- [X] T009 [P] Create `tests/KeeDroidSign.Core.Tests/Support/SecretLeakScanner.cs`: helper `AssertNoLeak(IEnumerable<string> secrets, params string[] texts)` that fails if any secret (length ≥ 4) appears in any text, case-sensitive; plus `Collect(Exception)` returning `Message`, `ToString()` and all inner exception text

**Checkpoint**: Foundation ready — user stories can start in parallel

---

## Phase 3: User Story 1 - Generate strong passwords (Priority: P1) 🎯 MVP

**Goal**: CSPRNG password generator honoring configurable rules and the CI-safe character set

**Independent Test**: `dotnet test --filter "FullyQualifiedName~Passwords"` — length, class
coverage, forbidden-character sampling, unsatisfiable rules

### Tests for User Story 1 ⚠️ write first, must fail

- [X] T010 [P] [US1] Write `tests/KeeDroidSign.Core.Tests/Passwords/PasswordPolicyTests.cs`: defaults (32, all classes on, ExcludeAmbiguous off); `Validate()` throws `ArgumentException` for length 7 and 129, for no class enabled, and for Length < number of enabled classes
- [X] T011 [P] [US1] Write `tests/KeeDroidSign.Core.Tests/Passwords/PasswordGeneratorTests.cs`: exact length for 8/32/128; letters+digits-only policy produces only `[A-Za-z0-9]`; every enabled class present in each of 1,000 samples; 10,000 default samples never contain `$`, backtick, `\`, `"`, `'`, or whitespace and every symbol ∈ `!#%+,-./:=?@^_~`; ExcludeAmbiguous never yields `0 O 1 l I`; 1,000 samples are all distinct

### Implementation for User Story 1

- [X] T012 [US1] Implement `src/KeeDroidSign.Core/Passwords/PasswordPolicy.cs` per data-model.md (properties with defaults, `Validate()` with descriptive messages)
- [X] T013 [US1] Implement `src/KeeDroidSign.Core/Passwords/IPasswordGenerator.cs` and `PasswordGenerator.cs` per research R7: `public const string SafeSymbols = "!#%+,-./:=?@^_~"`; `RNGCryptoServiceProvider` with rejection sampling (no modulo bias); one char from each enabled class first, remainder from the union, then Fisher–Yates shuffle with the same RNG; calls `policy.Validate()` first
- [X] T014 [US1] Run Passwords tests until green

**Checkpoint**: US1 complete and independently verifiable

---

## Phase 4: User Story 2 - Generate a signing key and keystore file (Priority: P1)

**Goal**: In-process RSA key + self-signed certificate written into a JKS keystore in memory

**Independent Test**: `dotnet test --filter "FullyQualifiedName~Keystore"` — generated keystore
reopens with both passwords; alias, key size, subject and validity match; no disk writes

### Tests for User Story 2 ⚠️ write first, must fail

- [X] T015 [P] [US2] Write `tests/KeeDroidSign.Core.Tests/Keystore/DistinguishedNameTests.cs`: CN required; Country must be 2 ASCII letters and is upper-cased; field length limits from data-model.md; values containing `, + " = ;` and Cyrillic letters round-trip exactly through `ToX509Name()` → read back attribute values; `ToRfc4514()` escapes `,` `+` `"` `\` `<` `>` `;`, leading `#`/space, trailing space
- [X] T016 [P] [US2] Write `tests/KeeDroidSign.Core.Tests/Keystore/KeyRequestTests.cs`: defaults (RSA 4096, 30 years); KeySize only 2048/3072/4096; ValidityYears 25–100; alias 1–64 chars `[A-Za-z0-9._-]`; passwords ≥ 6 chars and without control characters; `ToString()` contains neither password
- [X] T017 [P] [US2] Write `tests/KeeDroidSign.Core.Tests/Keystore/KeystoreGeneratorTests.cs` (use KeySize 2048 for speed except one 4096 test): result opens via `KeystoreReader.Open` with store+key passwords; exactly one key entry under the alias; RSA modulus bit length = requested; certificate subject equals request; `NotAfter ≥ now + years − 1 day`; signature algorithm SHA256withRSA; separate store/key passwords both enforced (wrong store → `InvalidKeystorePasswordException`, wrong key → `InvalidKeyPasswordException`, unknown alias → `AliasNotFoundException`); `GenerateAsync(request, ct)` creates no files (compare temp dir + working dir listings before/after); `GenerateAsync(request, path, ct)` writes the file and refuses an existing path with `KeystoreExistsException`; pre-cancelled token → `OperationCanceledException`; `SigningKeystore.ToBase64()` has no line breaks and decodes to identical bytes; `SigningKeystore.ToString()` leaks no password
- [X] T018 [P] [US2] Create `tests/KeeDroidSign.Core.Tests/Support/KeytoolFactAttribute.cs`: xUnit `FactAttribute` subclass that sets `Skip = "keytool not found"` unless keytool is found via env `KDS_KEYTOOL`, `JAVA_HOME\bin\keytool.exe`, or `PATH`; exposes static `KeytoolPath`
- [X] T019 [US2] Write `tests/KeeDroidSign.Core.Tests/Keystore/KeytoolInteropTests.cs` with `[KeytoolFact]`: generate keystore, save to a test temp dir, run `keytool -list -v -keystore <f> -storepass:env KDS_SP` (env var on the child process) → exit code 0, output contains the alias and `PrivateKeyEntry`; run `keytool -certreq -alias <a> -keystore <f> -storepass:env KDS_SP -keypass:env KDS_KP` → exit code 0 (proves the separate key password works); delete the temp dir in `finally`; when `KDS_KEEP_TEST_OUTPUT=1` also copy to `artifacts/test-output/sample.jks` and print its SHA-256 fingerprint

### Implementation for User Story 2

- [X] T020 [P] [US2] Implement `src/KeeDroidSign.Core/Keystore/DistinguishedName.cs` per data-model.md and research R6: validation, `internal X509Name ToX509Name()` built from ordered OID/value vectors (CN, OU, O, L, ST, C; omit empty), `ToRfc4514()` display string
- [X] T021 [P] [US2] Implement `src/KeeDroidSign.Core/Keystore/KeyRequest.cs` per data-model.md with `Validate()` and redacting `ToString()`
- [X] T022 [P] [US2] Implement `src/KeeDroidSign.Core/Keystore/SigningKeystore.cs`: `Content` (byte[]), `Alias`, `StorePassword`, `KeyPassword`, `CertificateDer` (byte[]), `NotBefore`/`NotAfter` (UTC), `ToBase64()` = `Convert.ToBase64String(Content)` (single line), redacting `ToString()`
- [X] T023 [US2] Implement `src/KeeDroidSign.Core/Keystore/KeystoreReader.cs` with BouncyCastle `JksStore`: `Open(content, alias, storePassword, keyPassword)` → `SigningKeystore` (map integrity failure → `InvalidKeystorePasswordException`, missing alias → `AliasNotFoundException`, key decryption failure → `InvalidKeyPasswordException`); `GetCertificate(content, alias, storePassword)` → DER bytes
- [X] T024 [US2] Implement `src/KeeDroidSign.Core/Keystore/IKeystoreGenerator.cs` and `KeystoreGenerator.cs` per research R2: `RsaKeyPairGenerator` (exponent 65537, `SecureRandom`), `X509V3CertificateGenerator` (random positive 128-bit serial, issuer = subject, NotBefore = UtcNow − 1 day, NotAfter = NotBefore + ValidityYears), `Asn1SignatureFactory("SHA256WITHRSA")`, `JksStore.SetKeyEntry(alias, key, keyPassword, chain)`, `Save(MemoryStream, storePassword)`; run CPU work via `Task.Run` honoring the token (check before/after key generation); overload with `outputPath` writes via `FileMode.CreateNew` (map `IOException` on existing file → `KeystoreExistsException`)
- [X] T025 [US2] Run Keystore tests until green (interop test passes when JDK present, else skipped)

**Checkpoint**: US1 + US2 complete — passwords and keystores can be produced offline

---

## Phase 5: User Story 3 - Certificate fingerprint for the Developer Console (Priority: P2)

**Goal**: SHA-256/SHA-1 fingerprints formatted as `AA:BB:...`

**Independent Test**: `dotnet test --filter "FullyQualifiedName~Fingerprints"` — fixture
certificate fingerprints equal reference values

### Tests for User Story 3 ⚠️ write first, must fail

- [X] T026 [P] [US3] Create fixture `tests/KeeDroidSign.Core.Tests/Fixtures/test-cert.der` (self-signed test certificate, no private key) and `Fixtures/test-cert.fingerprints.txt` with its SHA-256 and SHA-1 values obtained independently (`keytool -printcert -file test-cert.der` or `certutil -hashfile test-cert.der SHA256`, upper-cased and colon-joined); mark both as `CopyToOutputDirectory=PreserveNewest` in the test csproj
- [X] T027 [P] [US3] Write `tests/KeeDroidSign.Core.Tests/Fingerprints/CertificateFingerprintTests.cs`: fixture SHA-256 → 32 pairs equal reference; SHA-1 → 20 pairs equal reference; `Format(new byte[]{0x0A,0xFF})` = `"0A:FF"`; no leading/trailing `:`; regex `^([0-9A-F]{2}:)*[0-9A-F]{2}$`; end-to-end: generate keystore (US2) → `KeystoreReader.GetCertificate` → `Compute` equals SHA-256 of `SigningKeystore.CertificateDer`; wrong store password → `InvalidKeystorePasswordException`, unknown alias → `AliasNotFoundException`

### Implementation for User Story 3

- [X] T028 [US3] Implement `src/KeeDroidSign.Core/Fingerprints/CertificateFingerprint.cs`: `enum FingerprintAlgorithm { Sha256, Sha1 }`, `Compute(byte[] der, FingerprintAlgorithm = Sha256)` using `SHA256`/`SHA1` from `System.Security.Cryptography`, `Format(byte[])` → uppercase hex joined by `:`
- [X] T029 [US3] Run Fingerprints tests until green

**Checkpoint**: US3 complete

---

## Phase 6: User Story 4 - Authenticate with GitHub (Priority: P2)

**Goal**: Validate a personal access token, report login, check repository secret access

**Independent Test**: `dotnet test --filter "FullyQualifiedName~GitHubClientTests"` against the
fake handler — all response mappings in contracts/github-http.md

### Tests for User Story 4 ⚠️ write first, must fail

- [X] T030 [P] [US4] Create `tests/KeeDroidSign.Core.Tests/Support/FakeGitHubHandler.cs`: `HttpMessageHandler` with a route table `(HttpMethod, path+query) → (status, json, headers)`, records every request (method, URI, headers, body) for assertions, returns 404 for unknown routes, can simulate `HttpRequestException` and timeouts
- [X] T031 [P] [US4] Write `tests/KeeDroidSign.Core.Tests/GitHub/RepositoryTargetTests.cs`: parses `owner/name`, `https://github.com/owner/name`, `https://github.com/owner/name.git`, trailing slash; rejects empty parts, invalid characters, owner > 39 chars, name > 100 chars
- [X] T032 [P] [US4] Write `tests/KeeDroidSign.Core.Tests/GitHub/GitHubClientAuthTests.cs`: every request has `Authorization: Bearer <token>`, `Accept: application/vnd.github+json`, `X-GitHub-Api-Version: 2022-11-28`, `User-Agent: KeeDroidSign/…`, and URI host `api.github.com` over https; `ValidateTokenAsync` maps 200→`Valid`+login+`X-OAuth-Scopes`, 401→`InvalidOrExpired`, 403 with `x-ratelimit-remaining: 0`→`RateLimited`, 429→`RateLimited`, other 403→`InsufficientPermissions`, `HttpRequestException`→`NetworkError`; `CheckRepositoryAccessAsync` maps repo 404→`NotFoundOrNoAccess`, `archived:true`→`Archived`, secrets-list 200→`CanManageSecrets`, secrets-list 403/404→`InsufficientPermissions`; token never appears in any exception or result `ToString()` (use `SecretLeakScanner`); `GitHubCredential.ToString()` redacts

### Implementation for User Story 4

- [X] T033 [P] [US4] Implement `src/KeeDroidSign.Core/GitHub/GitHubCredential.cs` (token holder, redacting `ToString()`, rejects empty/whitespace token) and `GitHubIdentity.cs`
- [X] T034 [P] [US4] Implement `src/KeeDroidSign.Core/GitHub/RepositoryTarget.cs` with `Parse(string)`/`TryParse` and validation per data-model.md
- [X] T035 [P] [US4] Implement `src/KeeDroidSign.Core/GitHub/GitHubModels.cs`: `[DataContract]` DTOs `UserDto { login }`, `RepositoryDto { archived }`, `SecretListDto { total_count, secrets[] { name } }`, `PublicKeyDto { key_id, key }`, `PutSecretDto { encrypted_value, key_id }`; enums `TokenStatus`, `RepositoryAccess`, `SecretWriteStatus` per data-model.md; internal `Json.Deserialize<T>/Serialize<T>` helpers over `DataContractJsonSerializer`
- [X] T036 [US4] Implement `src/KeeDroidSign.Core/GitHub/GitHubClient.cs` (part 1): constructor `(GitHubCredential, HttpMessageHandler handler = null)` with fixed base `https://api.github.com/`, default headers per contracts/github-http.md, 30 s timeout, `IDisposable`; `ValidateTokenAsync` and `CheckRepositoryAccessAsync` with the status mappings; never include response bodies or the token in exceptions
- [X] T037 [US4] Run GitHub auth tests until green

**Checkpoint**: US4 complete

---

## Phase 7: User Story 5 - Add or update repository secrets (Priority: P2)

**Goal**: Encrypt with sealed box and create/update the four Actions secrets with conflict control

**Independent Test**: `dotnet test --filter "FullyQualifiedName~SealedBox|FullyQualifiedName~SecretExporter|FullyQualifiedName~SecretNames"`

**Depends on**: US4 (`GitHubClient`), US2 (`SigningKeystore`)

### Tests for User Story 5 ⚠️ write first, must fail

- [X] T038 [P] [US5] Write `tests/KeeDroidSign.Core.Tests/GitHub/HSalsa20Tests.cs` with the libsodium/NaCl published test vector (`crypto_core_hsalsa20` from NaCl `core1` test: key = shared secret `4a5d9d5ba4ce2de1728e3bf480350f25e07e21c947d19e3376f09b3c1e161742`, input = 16 zero bytes → firstkey `1b27556473e985d462cd51197a9a46c76009549eac6474f206c4ee0844f68389`; source: libsodium `test/default/core1.c` / `core1.exp`, verified 2026-10-08)
- [X] T039 [P] [US5] Write `tests/KeeDroidSign.Core.Tests/GitHub/SealedBoxTests.cs`: output length = message + 48; two seals of the same message differ; `Sodium.SealedPublicKeyBox.Open` (test-only Sodium.Core) decrypts our output for messages of 0, 1, 32 and 40,000 bytes; wrong recipient key fails to open; recipient key length ≠ 32 → `ArgumentException`
- [X] T040 [P] [US5] Write `tests/KeeDroidSign.Core.Tests/GitHub/SecretNamesTests.cs`: defaults equal `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`; rejects names starting with a digit, containing `-` or space, starting with `GITHUB_` (any case), and duplicate names (case-insensitive)
- [X] T041 [P] [US5] Write `tests/KeeDroidSign.Core.Tests/GitHub/GitHubClientSecretsTests.cs`: `ListSecretNamesAsync` paginates (`per_page=100`, pages until `total_count`); `GetPublicKeyAsync` rejects decoded key length ≠ 32; `PutSecretAsync` sends `PUT …/actions/secrets/{name}` with JSON `{encrypted_value,key_id}` and maps 201→`Created`, 204→`Updated`, 403/404/422/429/network → `Failed` with fixed reasons; response body text never copied into reasons
- [X] T042 [P] [US5] Write `tests/KeeDroidSign.Core.Tests/GitHub/SecretExporterTests.cs` (fake handler holding a real X25519 key pair so uploaded values are decrypted with Sodium.Core and compared): empty repo → 4×`Created` with correct plaintexts (Base64 keystore single-line, store password, alias, key password); existing names + `overwrite:false` → all `SkippedConflict` and zero PUT requests; `overwrite:true` → existing ones `Updated`; custom `SecretMapping` names used; one PUT failing → others still attempted, failure reported with reason; `PlanAsync` returns correct `ToCreate`/`ToOverwrite`; value > 48 KB rejected before any request; no secret plaintext in any recorded request body, result or exception (`SecretLeakScanner`)

### Implementation for User Story 5

- [X] T043 [P] [US5] Implement `src/KeeDroidSign.Core/GitHub/HSalsa20.cs`: internal static `byte[] Derive(byte[] key32, byte[] input16)` — Salsa20 core 20 rounds, output words 0,5,10,15,6,7,8,9 (little-endian), constants "expand 32-byte k"
- [X] T044 [US5] Implement `src/KeeDroidSign.Core/GitHub/SealedBox.cs` per research R4: ephemeral X25519 key pair (`X25519KeyPairGenerator`); `nonce = Blake2bDigest(192)` over `epk ‖ pk`; `shared = X25519Agreement`; `k = HSalsa20.Derive(shared, 0¹⁶)`; XSalsa20 (`XSalsa20Engine`, key k, 24-byte nonce) keystream: first 32 bytes → Poly1305 key, remaining stream XOR message; `tag = Poly1305(ciphertext)`; return `epk ‖ tag ‖ ciphertext`; zero intermediate key buffers
- [X] T045 [P] [US5] Implement `src/KeeDroidSign.Core/GitHub/SecretMapping.cs` (four names with defaults) and `SecretNames.cs` (`Validate(SecretMapping)` per FR-024)
- [X] T046 [US5] Extend `src/KeeDroidSign.Core/GitHub/GitHubClient.cs` (part 2): `ListSecretNamesAsync`, `GetPublicKeyAsync` (returns `RepositoryPublicKey { KeyId, Key }`), `PutSecretAsync` per contracts/github-http.md
- [X] T047 [US5] Implement `src/KeeDroidSign.Core/GitHub/SecretExporter.cs` with `ExportPlan`, `SecretOutcome`, `ExportResult` types: `PlanAsync`; `ExportAsync(repo, keystore, mapping, overwrite, ct)` — validate names, build values (`keystore.ToBase64()`, passwords, alias), reject values > 48 KB, refuse on conflicts without `overwrite`, fetch public key once, seal + PUT each secret, collect per-secret outcomes, honor cancellation between secrets
- [X] T048 [US5] Run US5 tests until green

**Checkpoint**: US5 complete — full export flow works against the fake GitHub

---

## Phase 8: User Story 6 - Automated test suite (Priority: P1)

**Goal**: The whole suite runs offline and proves no secret leakage and CI compatibility

**Independent Test**: `dotnet test KeeDroidSign.sln` with network disabled — all pass (interop
test may be skipped)

- [X] T049 [P] [US6] Write `tests/KeeDroidSign.Core.Tests/CrossCutting/SecretLeakTests.cs`: drive every failure path (bad policies, bad passwords, wrong alias, all GitHub error mappings, cancelled operations) with known secret values and assert via `SecretLeakScanner` that no exception message/`ToString()`/result text contains them (SC-007)
- [X] T050 [P] [US6] Write `tests/KeeDroidSign.Core.Tests/CrossCutting/CiContractTests.cs`: generated passwords pass a simulated unquoted-heredoc + `.properties` round-trip (no `$`, backtick, `\`, quotes, whitespace; `java.util.Properties`-style parse yields the same value); `ToBase64()` output matches regex `^[A-Za-z0-9+/]+=*$` (single line) and decodes to the keystore bytes
- [X] T051 [P] [US6] Write `tests/KeeDroidSign.Core.Tests/CrossCutting/OfflineGuardTests.cs`: assert no production type creates `HttpClient` without the injected handler in tests (construct `GitHubClient` with fake handler and verify zero requests escape to real network by checking the handler recorded all calls)
- [X] T052 [US6] Run full suite `dotnet test KeeDroidSign.sln -c Release`; record pass/skip counts in `specs/001-core-signing-modules/quickstart.md` expected-results note if they differ from the documented expectations

**Checkpoint**: All stories verified by an offline suite

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T053 [P] Add XML doc comments to all public types/members in `src/KeeDroidSign.Core/` and enable `GenerateDocumentationFile` in `KeeDroidSign.Core.csproj`
- [X] T054 [P] Create `README.md` at repo root (English): project purpose, build/test commands from quickstart.md, the GitHub Actions secret contract (four secret names + reference workflow snippet from spec Clarifications), required PAT permissions (Secrets read/write, Metadata read; classic: `repo`)
- [X] T055 Review all changed files against Constitution Principles I–VI (English-only, no `keepass/` changes — `git diff --stat keepass` empty, no secrets in logs/messages, scope) and fix deviations
- [X] T056 Execute quickstart.md sections 1–4 and confirm documented outcomes

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** → **Foundational (Phase 2)** → user stories
- **US1 (Phase 3)**: depends only on Phase 2
- **US2 (Phase 4)**: depends only on Phase 2
- **US3 (Phase 5)**: fixture tests depend only on Phase 2; the keystore end-to-end case in T027 depends on US2
- **US4 (Phase 6)**: depends only on Phase 2
- **US5 (Phase 7)**: depends on US4 (GitHubClient) and US2 (SigningKeystore)
- **US6 (Phase 8)**: depends on US1–US5 (cross-cutting verification of all modules)
- **Polish (Phase 9)**: after all stories

### Story Graph

```text
Setup → Foundational ─┬─> US1 ─────────────────────────┐
                      ├─> US2 ─┬─> US3 ────────────────┤
                      │        └──────────┐            ├─> US6 → Polish
                      └─> US4 ────────────┴─> US5 ─────┘
```

### Within Each Story

Tests first (must fail) → value types/models → services → run tests green.

## Parallel Opportunities

- Phase 1: T005 alongside T001–T004
- Phase 2: T008, T009 in parallel after T007
- After Phase 2: US1, US2 and US4 can be developed in parallel by different agents
- US1: T010 ‖ T011
- US2: T015 ‖ T016 ‖ T017 ‖ T018, then T020 ‖ T021 ‖ T022
- US3: T026 ‖ T027
- US4: T030 ‖ T031 ‖ T032, then T033 ‖ T034 ‖ T035
- US5: T038 ‖ T039 ‖ T040 ‖ T041 ‖ T042, then T043 ‖ T045
- US6: T049 ‖ T050 ‖ T051
- Polish: T053 ‖ T054

### Parallel Example: User Story 2

```text
Task: "Write DistinguishedNameTests in tests/KeeDroidSign.Core.Tests/Keystore/DistinguishedNameTests.cs"
Task: "Write KeyRequestTests in tests/KeeDroidSign.Core.Tests/Keystore/KeyRequestTests.cs"
Task: "Write KeystoreGeneratorTests in tests/KeeDroidSign.Core.Tests/Keystore/KeystoreGeneratorTests.cs"
Task: "Create KeytoolFactAttribute in tests/KeeDroidSign.Core.Tests/Support/KeytoolFactAttribute.cs"
```

### Parallel Example: User Story 5

```text
Task: "Write HSalsa20Tests in tests/KeeDroidSign.Core.Tests/GitHub/HSalsa20Tests.cs"
Task: "Write SealedBoxTests in tests/KeeDroidSign.Core.Tests/GitHub/SealedBoxTests.cs"
Task: "Write SecretNamesTests in tests/KeeDroidSign.Core.Tests/GitHub/SecretNamesTests.cs"
Task: "Write GitHubClientSecretsTests in tests/KeeDroidSign.Core.Tests/GitHub/GitHubClientSecretsTests.cs"
Task: "Write SecretExporterTests in tests/KeeDroidSign.Core.Tests/GitHub/SecretExporterTests.cs"
```

## Implementation Strategy

### MVP First

1. Phase 1 + Phase 2
2. US1 (passwords) → validate
3. US2 (keystore) → validate — this is the minimal useful core: passwords + JKS without Java

### Incremental Delivery

1. MVP (US1 + US2)
2. + US3 → fingerprint for the Android Developer Console
3. + US4 → GitHub token validation
4. + US5 → secret export (full CI integration)
5. + US6 → cross-cutting verification, then Polish

## Notes

- [P] = different files, no dependencies on incomplete tasks
- Every test that handles secrets uses obviously fake values (e.g. `test-store-pass`)
- Never commit generated keystores except the public `test-cert.der` fixture
- Commit after each task or logical group
