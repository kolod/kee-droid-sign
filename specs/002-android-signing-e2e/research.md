# Research: Android Signing End-to-End Test

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-10-08

Reference project: `kolod/whiskergrid` (`release.yml`, `app/build.gradle.kts`, version catalog).

## R1. Android toolchain versions

- **Decision**: Same as the reference project — Android Gradle Plugin 9.4.1 (built-in Kotlin
  support, no separate Kotlin plugin needed), Gradle 9.8.1 wrapper, `compileSdk`/`targetSdk` 37,
  `minSdk` 26. Daemon JVM pinned to **JDK 21** via `gradle/gradle-daemon-jvm.properties` (the
  reference uses 25); CI uses Temurin 21.
- **Rationale**: Known-good combination already building and signing in the maintainer's other
  project. JDK 21 is the JDK installed on the development machine, so no toolchain download is
  needed locally; AGP 9.x requires JDK 17+.
- **Alternatives considered**: JDK 25 exactly like the reference (forces a download locally);
  older AGP 8.x (needs the separate Kotlin Android plugin).

## R2. Minimal app without dependencies

- **Decision**: One `Activity` subclass (`android.app.Activity`, not AndroidX) that calls
  `setContentView(TextView(this).apply { text = getString(R.string.hello) })`. No AndroidX,
  Compose, launcher icon or theme resources; platform theme `@android:style/Theme.Material.Light`.
  Application ID `io.github.kolod.keedroidsign.sample`.
- **Rationale**: Zero library dependencies → fastest CI build, nothing to update, smallest APK, and
  no permissions (FR-003). The app exists only to be signed.
- **Alternatives considered**: Compose like the reference (dozens of dependencies for one string).

## R3. Signing configuration and "no debug fallback"

- **Decision**: `app/build.gradle.kts` loads `keystore.properties` like the reference and creates a
  `release` signing config when it exists. Unlike the reference, when it is missing the release
  build type has **no** signing config and a check task wired before `assembleRelease` /
  `packageRelease` fails with "Release signing is not configured: keystore.properties not found"
  (FR-005). Debug builds are unaffected (FR-002). `minifyEnabled` stays off (nothing to shrink).
- **Rationale**: Silent fallback to the debug key would let the end-to-end test pass with the wrong
  key. Failing in a task (not at configuration time) keeps `assembleDebug` working without secrets.

## R4. Workflow design (`.github/workflows/android-sign.yml`)

- **Decision**:
  - Triggers: `workflow_dispatch` (input `request_id`, optional) and `push` to `main` with paths
    `android/**` and the workflow file; `pull_request` builds **debug only** (no secrets needed,
    covers fork PRs — FR-008).
  - `run-name: Sign sample APK ${{ inputs.request_id || github.sha }}` so runs are identifiable
    (FR-011).
  - Steps (signing job): checkout → wrapper validation → setup-java 21 → setup-gradle →
    **check secrets** (fails with `::error::` naming each missing secret) → decode keystore →
    write `keystore.properties` (reference heredoc, unchanged) → `:app:assembleRelease` →
    `apksigner verify --print-certs` → convert the `SHA-256 digest` line to `AA:BB:...` → write
    `signing-report.json` + job summary → upload artifacts `sample-apk` and `signing-report` →
    `if: always()` cleanup of keystore and properties (FR-010).
  - `permissions: contents: read` (least privilege).
- **Rationale**: Mirrors the reference so the plugin's secret contract is exercised exactly as real
  projects use it; adds verification and a machine-readable report for the live test.
- **Alternatives considered**: Publishing a GitHub Release (spec: artifact only); emitting the
  fingerprint only in logs (not retrievable reliably via API).

## R5. Fingerprint extraction in CI

- **Decision**: `apksigner` from the runner's Android SDK build-tools (latest installed version
  directory). Its output line `Signer #1 certificate SHA-256 digest: <64 hex>` is upper-cased and
  colon-joined by a small shell step; the step fails if the number of signers ≠ 1 (spec US2-5).
- **Rationale**: `apksigner` is the official verifier and checks v2/v3 signatures the APK actually
  uses (v1 is disabled for `minSdk ≥ 24`).

## R6. Live test location and GitHub Actions API

- **Decision**: New test project `tests/KeeDroidSign.LiveTests` (net48, xUnit) so the default
  `KeeDroidSign.Core.Tests` suite stays strictly offline. It reuses `KeystoreGenerator`,
  `GitHubClient`, `SecretExporter` and `CertificateFingerprint` from the core; Actions-specific
  calls live in a **test-only** helper `GitHubActionsHelper` (not in the core, Constitution IV):
  - `POST /repos/{o}/{r}/actions/workflows/android-sign.yml/dispatches` with
    `{"ref":"main","inputs":{"request_id":"<guid>"}}` and header
    `X-GitHub-Api-Version: 2026-03-10` → **200** with `workflow_run_id`, `html_url` (this API version
    always returns run details; verified in GitHub REST docs).
  - Poll `GET /repos/{o}/{r}/actions/runs/{run_id}` every 15 s until `status == completed`
    (timeout 20 min, FR-014).
  - `GET /repos/{o}/{r}/actions/runs/{run_id}/artifacts` → artifact `signing-report` →
    `GET /repos/{o}/{r}/actions/artifacts/{id}/zip` with auto-redirect **disabled**; the `Location`
    URL (pre-signed, 1-minute lifetime) is fetched without the token.
- **Rationale**: The returned run ID removes the race between concurrent runs (spec edge case 1);
  `request_id` is still passed and checked in `display_title` as a second guard.
- **Opt-in**: `[LiveFact]` skips unless `KDS_LIVE_GITHUB_TOKEN` is set (FR-013); repository from
  `KDS_LIVE_REPOSITORY` (default `kolod/kee-droid-sign`, FR-016); ref from `KDS_LIVE_REF`
  (default `main`).
- **Token permissions**: fine-grained — *Actions: read and write*, *Secrets: read and write*,
  *Metadata: read*; classic — `repo`, `workflow`.

## R7. Workflow on a feature branch

- **Decision**: `workflow_dispatch` only works for workflow files present on the default branch, so
  the live test can run only after this feature is merged into `main`. Before merging, the workflow
  is validated by the `push`/`pull_request` triggers on the PR and the signing path manually with
  `KDS_LIVE_REF` set to the feature branch once the file exists on `main`.
- **Rationale**: GitHub limitation; documented in quickstart so the first live run is not confusing.

## R8. Secret hygiene in the live test

- **Decision**: Generated passwords use the feature-001 generator; the keystore exists only in
  memory; test output prints the run URL, the expected and actual fingerprints (public data) and
  never passwords, token or Base64. All exceptions pass through `SecretLeakScanner` before being
  rethrown in assertions (FR-015).
