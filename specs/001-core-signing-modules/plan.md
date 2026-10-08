# Implementation Plan: Core Signing Modules

**Branch**: `001-core-signing-modules` | **Date**: 2026-10-08 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-core-signing-modules/spec.md`

## Summary

Build `KeeDroidSign.Core`, a UI- and KeePass-independent .NET Framework 4.8 class library that
(1) generates CI-safe passwords, (2) creates an Android signing keystore (JKS, RSA 4096, 30 years)
entirely in-process with BouncyCastle — no Java, no key material on disk, (3) computes SHA-256/SHA-1
certificate fingerprints in `AA:BB:...` format for the Android Developer Console, (4) validates a
GitHub personal access token, and (5) creates/updates repository Actions secrets encrypted with a
managed libsodium sealed-box implementation. An xUnit test project verifies every module offline,
with GitHub simulated by a fake HTTP handler. See [research.md](./research.md) for decisions.

## Technical Context

**Language/Version**: C# with `LangVersion` pinned to 12 (only features that need no newer
runtime), target framework `net48`

**Primary Dependencies**: BouncyCastle.Cryptography 2.x (JKS read, X25519, XSalsa20, Poly1305,
BLAKE2b, RSA key generation, X.509 certificates, JKS read/write); framework `System.Net.Http`,
`System.Runtime.Serialization` (JSON); no external tools

**Storage**: None in the core; results returned to the caller for storage in the KeePass
database (later feature)

**Testing**: xUnit 2.x on net48 via `dotnet test`; fake `HttpMessageHandler`; `Sodium.Core`
(test-only) to cross-check sealed boxes; one optional JDK `keytool` interoperability test (skippable)

**Target Platform**: Windows, KeePass 2.61.x running on CLR 4 (.NET Framework 4.8)

**Project Type**: Class library + test project (plugin shell and UI are later features)

**Performance Goals**: Password generation < 10 ms; keystore generation + fingerprint < 30 s
(SC-003, dominated by RSA 4096 prime generation); export of 4 secrets < 15 s (SC-004)

**Constraints**: No secrets on disk after operations, in logs, exceptions or command lines;
HTTPS only; offline test suite; single managed dependency deployed next to the plugin

**Scale/Scope**: One keystore and up to ~10 secrets per operation; ~15 production classes

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Gate | Pre-research | Post-design |
|-----------|------|--------------|-------------|
| I. English-only | All code, docs, specs in English | ✅ | ✅ all artifacts English |
| II. KeePass plugin architecture | No submodule changes; integration via plugin API only | ✅ core has no KeePass reference; submodule untouched | ✅ |
| III. Secrets protected (NON-NEGOTIABLE) | No secrets in logs/settings/disk; temp cleanup; HTTPS + sealed box; overwrite needs confirmation | ✅ | ✅ R2 in-memory generation (no temp files at all), R4 sealed box, R5 HTTPS-only base URL, `overwrite` flag (FR-025), redacting `ToString()` (R9) |
| IV. Defined scope | Only key generation, GitHub export, fingerprint | ✅ | ✅ |
| V. Configurable tooling | Tool paths configurable if any external tool is used | ✅ N/A — feature invokes no external tools | ✅ N/A (R2) |
| VI. Testable core, thin UI | Logic outside WinForms; tests for security-critical logic; dependencies justified | ✅ | ✅ test project covers sealed box, no-disk-write guarantee, secret leakage; BouncyCastle justified in R3/R4 |

Result: **PASS** — no violations; Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/001-core-signing-modules/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── core-api.md      # Public C# API of KeeDroidSign.Core
│   └── github-http.md   # GitHub REST interactions consumed
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
KeeDroidSign.sln
Directory.Build.props            # net48, nullable off, warnings as errors, common metadata
keepass/                         # git submodule (read-only reference)
src/
└── KeeDroidSign.Core/
    ├── KeeDroidSign.Core.csproj
    ├── KeeDroidSignException.cs
    ├── Passwords/
    │   ├── PasswordPolicy.cs
    │   └── PasswordGenerator.cs
    ├── Keystore/
    │   ├── DistinguishedName.cs
    │   ├── KeyRequest.cs
    │   ├── SigningKeystore.cs
    │   ├── IKeystoreGenerator.cs
    │   ├── KeystoreGenerator.cs
    │   └── KeystoreReader.cs
    ├── Fingerprints/
    │   └── CertificateFingerprint.cs
    └── GitHub/
        ├── GitHubCredential.cs
        ├── RepositoryTarget.cs
        ├── GitHubClient.cs
        ├── GitHubModels.cs      # DataContract DTOs
        ├── SealedBox.cs
        ├── HSalsa20.cs
        ├── SecretMapping.cs
        ├── SecretNames.cs
        └── SecretExporter.cs
tests/
└── KeeDroidSign.Core.Tests/
    ├── KeeDroidSign.Core.Tests.csproj
    ├── Fixtures/                # test certificate + expected fingerprints, test JKS
    ├── Support/                 # FakeGitHubHandler, KeytoolFactAttribute, SecretLeakScanner
    ├── Passwords/
    ├── Keystore/
    ├── Fingerprints/
    └── GitHub/
```

**Structure Decision**: Single class library plus one test project at the repository root.
The future KeePass plugin project (`src/KeeDroidSign/`, UI + plugin entry point) will reference
`KeeDroidSign.Core`; it is out of scope here. The `keepass/` submodule is not referenced by the
core.

## Complexity Tracking

No constitution violations — section intentionally empty.
