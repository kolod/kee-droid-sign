# Implementation Plan: Android Signing End-to-End Test

**Branch**: `002-android-signing-e2e` | **Date**: 2026-10-08 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-android-signing-e2e/spec.md`

## Summary

Add a dependency-free "Hello, World!" Android app in Kotlin under `android/`, a GitHub Actions
workflow that builds and signs its release APK from the four repository secrets defined in feature
001 (following `kolod/whiskergrid`, but without the debug-key fallback and with signature
verification and a machine-readable signing report), and an opt-in live test project that uses the
plugin core to generate a key, export it to this repository's secrets, dispatch the workflow, and
check that the APK is signed with exactly that key. See [research.md](./research.md).

## Technical Context

**Language/Version**: Kotlin (AGP 9.4.1 built-in Kotlin) for the sample app; C# / net48 for the
live test; YAML for the workflow

**Primary Dependencies**: Android Gradle Plugin 9.4.1, Gradle 9.8.1 wrapper, no app libraries;
GitHub-hosted actions `checkout`, `setup-java`, `gradle/actions`, `upload-artifact`; live test reuses
`KeeDroidSign.Core` (feature 001) + xUnit

**Storage**: N/A (secrets live in GitHub; keystore only in memory / runner temp)

**Testing**: Gradle build checks for the app; workflow self-verification (`apksigner`); opt-in
xUnit live test against real GitHub

**Target Platform**: Android 8.0+ (minSdk 26, target 37); `ubuntu-latest` runners; Windows for the
live test

**Project Type**: Test fixture (Android app) + CI workflow + integration test project

**Performance Goals**: Workflow signing job < 10 min; live test end-to-end < 20 min (SC-004)

**Constraints**: No debug-key fallback; no secrets committed or logged; default .NET test suite stays
offline; least-privilege workflow permissions

**Scale/Scope**: 1 activity, 1 workflow (2 jobs), 1 live test

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / rule | Gate | Pre-research | Post-design |
|------------------|------|--------------|-------------|
| I. English-only | Code, workflow, docs in English | ✅ | ✅ |
| II. Plugin architecture | No `keepass/` changes; plugin integration untouched | ✅ | ✅ nothing under `keepass/` or the plugin API |
| III. Secrets protected | No secrets committed/logged; cleanup; HTTPS + sealed box | ✅ | ✅ `keystore.properties`/`*.jks` git-ignored, `if: always()` cleanup, live test output limited to public data (R8), export via feature-001 sealed box |
| IV. Scope | Must relate to Android signing key management | ✅ | ✅ end-to-end verification of the export; Actions API calls kept in a test-only helper, not in the core |
| V. Tooling | External tools configurable if used by the plugin | ✅ N/A | ✅ N/A (apksigner used only by CI) |
| VI. Testable core | Logic outside UI; tests for security-critical paths | ✅ | ✅ live test exercises real export; offline suite unchanged |
| Tech: Kotlin fixtures (v1.3.0) | Kotlin, in `android/`, Gradle wrapper, not referenced by .NET solution, not distributed | ✅ | ✅ |

Result: **PASS** — no violations.

## Project Structure

### Documentation (this feature)

```text
specs/002-android-signing-e2e/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── workflow.md
│   └── live-test.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
.github/
└── workflows/
    └── android-sign.yml            # build-debug (PR) + sign (dispatch/push)
android/
├── .gitignore                      # .gradle/, build/, local.properties, keystore.properties, *.jks
├── settings.gradle.kts             # rootProject "keedroidsign-sample", include(":app")
├── build.gradle.kts
├── gradle.properties
├── gradlew, gradlew.bat
├── gradle/
│   ├── libs.versions.toml          # agp only
│   ├── gradle-daemon-jvm.properties  # JDK 21
│   └── wrapper/ (gradle-wrapper.jar, gradle-wrapper.properties: 9.8.1)
└── app/
    ├── build.gradle.kts            # keystore.properties signing, no debug fallback
    └── src/main/
        ├── AndroidManifest.xml
        ├── kotlin/io/github/kolod/keedroidsign/sample/MainActivity.kt
        └── res/values/strings.xml
tests/
└── KeeDroidSign.LiveTests/
    ├── KeeDroidSign.LiveTests.csproj  # net48, refs KeeDroidSign.Core, xUnit
    ├── LiveFactAttribute.cs           # skip unless KDS_LIVE_GITHUB_TOKEN
    ├── LiveSettings.cs                # env-var configuration
    ├── GitHubActionsHelper.cs         # dispatch, poll, artifacts, download (test-only)
    └── SigningEndToEndTests.cs
```

**Structure Decision**: The Android fixture is a self-contained Gradle build in `android/` (not
referenced by `KeeDroidSign.sln`); the live test is a separate .NET test project added to the
solution so it builds with everything else but runs (skipped) without a token. The workflow must
be at the repository root `.github/workflows/`.

## Complexity Tracking

No constitution violations — section intentionally empty.
