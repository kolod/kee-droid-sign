# Tasks: Android Signing End-to-End Test

**Input**: Design documents from `/specs/002-android-signing-e2e/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/workflow.md](./contracts/workflow.md),
[contracts/live-test.md](./contracts/live-test.md), [quickstart.md](./quickstart.md)

**Tests**: The live end-to-end test **is** the requested test (User Story 3). The app and workflow
are verified by build checks and the workflow's own signature verification; no separate unit-test
tasks are generated.

**Organization**: Tasks are grouped by user story so each story can be implemented and verified
independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User story the task belongs to (US1–US3)

## Path Conventions

- Android fixture: `android/` (self-contained Gradle build, Kotlin, never referenced by the .NET solution)
- Workflow: `.github/workflows/android-sign.yml`
- Live test: `tests/KeeDroidSign.LiveTests/`
- Everything in English (Constitution Principle I); no secrets committed (Principle III)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Gradle wrapper and root build files for the Android fixture

- [X] T001 Copy the Gradle wrapper from `kolod/whiskergrid` (`gradlew`, `gradlew.bat`, `gradle/wrapper/gradle-wrapper.jar`, `gradle/wrapper/gradle-wrapper.properties` with Gradle 9.8.1) into `android/` via `gh api repos/kolod/whiskergrid/contents/<path>`; keep `gradlew` executable in git (`git update-index --chmod=+x android/gradlew`)
- [X] T002 [P] Create `android/gradle/gradle-daemon-jvm.properties` with `toolchainVersion=21` (research R1)
- [X] T003 [P] Create `android/gradle/libs.versions.toml` with only `agp = "9.4.1"` and plugin `android-application = { id = "com.android.application", version.ref = "agp" }`
- [X] T004 [P] Create `android/settings.gradle.kts` (pluginManagement repos google/mavenCentral/gradlePluginPortal, foojay-resolver-convention 1.0.0, `RepositoriesMode.FAIL_ON_PROJECT_REPOS`, `rootProject.name = "keedroidsign-sample"`, `include(":app")`), `android/build.gradle.kts` (`alias(libs.plugins.android.application) apply false`) and `android/gradle.properties` (`org.gradle.jvmargs=-Xmx2g -Dfile.encoding=UTF-8`, `org.gradle.caching=true`)
- [X] T005 [P] Create `android/.gitignore` with `.gradle/`, `.kotlin/`, `build/`, `local.properties`, `.idea/`, `*.iml`, `captures/`, `keystore.properties`, `*.jks`, `*.keystore`
- [X] T006 Create local-only `android/local.properties` with `sdk.dir` pointing to the local Android SDK (git-ignored; not committed) and verify `git status` does not list it

**Checkpoint**: `android/gradlew --version` (run from `android/`) prints Gradle 9.8.1

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Nothing blocks all stories beyond Phase 1 — US1 is itself the foundation for US2 and US3.

---

## Phase 3: User Story 1 - Minimal test Android app (Priority: P1) 🎯 MVP

**Goal**: A dependency-free Kotlin app showing "Hello, World!" that builds into a debug APK without secrets

**Independent Test**: `cd android && ./gradlew :app:assembleDebug` produces `app/build/outputs/apk/debug/app-debug.apk`; launching it shows "Hello, World!" (quickstart §1)

### Implementation for User Story 1

- [X] T007 [P] [US1] Create `android/app/src/main/res/values/strings.xml` with `app_name` = "KeeDroidSign Sample" and `hello` = "Hello, World!"
- [X] T008 [P] [US1] Create `android/app/src/main/AndroidManifest.xml`: no `<uses-permission>`, `<application android:label="@string/app_name" android:theme="@android:style/Theme.Material.Light" android:allowBackup="false">` with exported launcher activity `.MainActivity`
- [X] T009 [P] [US1] Create `android/app/src/main/kotlin/io/github/kolod/keedroidsign/sample/MainActivity.kt`: `class MainActivity : android.app.Activity()` whose `onCreate` calls `setContentView(TextView(this).apply { text = getString(R.string.hello); textSize = 24f; gravity = Gravity.CENTER })`
- [X] T010 [US1] Create `android/app/build.gradle.kts`: plugin `alias(libs.plugins.android.application)`; `namespace`/`applicationId` = `io.github.kolod.keedroidsign.sample`; `compileSdk = 37`, `minSdk = 26`, `targetSdk = 37`; `versionCode`/`versionName` from `-PappVersionCode`/`-PappVersionName` with defaults 1 / "1.0"; Java 17 compile options; no dependencies (signing config is added in US2)
- [X] T011 [US1] Build `./gradlew :app:assembleDebug` in `android/` and confirm the APK exists; inspect it with `aapt2 dump badging` (from local build-tools) to confirm package name and no permissions

**Checkpoint**: US1 complete — debug APK builds with no secrets (SC-001)

---

## Phase 4: User Story 2 - CI workflow builds and signs the APK (Priority: P1)

**Goal**: Release builds sign only with the key from `keystore.properties`; a workflow produces a verified signed APK and a signing report

**Independent Test**: Locally: release build fails without `keystore.properties` and signs with a local throwaway keystore (quickstart §2–3). On GitHub: dispatching the workflow with valid secrets yields `sample-apk` + `signing-report` artifacts (quickstart §4)

### Implementation for User Story 2

- [X] T012 [US2] Extend `android/app/build.gradle.kts` with release signing per research R3: load `keystore.properties` from the app dir (like whiskergrid); when it has `storeFile`, create `signingConfigs.release` and assign it to `buildTypes.release`; otherwise assign **no** signing config and register a `verifyReleaseSigning` task that throws `GradleException("Release signing is not configured: keystore.properties not found")`, wired with `tasks.matching { it.name == "packageRelease" || it.name == "assembleRelease" }.configureEach { dependsOn("verifyReleaseSigning") }`; add a comment explaining why there is no debug fallback
- [X] T013 [US2] Verify locally: `./gradlew :app:assembleRelease` without `keystore.properties` fails with the message above; `./gradlew :app:assembleDebug` still succeeds
- [X] T014 [US2] Verify locally with a throwaway keystore (generated with `keytool -genkeypair -storetype JKS ...` into `android/app/release.keystore.jks` plus `android/app/keystore.properties`): release build succeeds, `apksigner verify --print-certs` shows exactly one signer whose SHA-256 equals `keytool -list -v` output; then delete both files and confirm `git status` never listed them
- [X] T015 [US2] Create `.github/workflows/android-sign.yml` per [contracts/workflow.md](./contracts/workflow.md): `name: Sign sample APK`; `run-name: Sign sample APK ${{ inputs.request_id || github.sha }}`; triggers `workflow_dispatch` (input `request_id`, string, default ""), `push` (branch main, paths `android/**`, `.github/workflows/android-sign.yml`), `pull_request` (same paths); `permissions: contents: read`; `defaults.run.working-directory: android`
- [X] T016 [US2] In `.github/workflows/android-sign.yml` add job `build-debug` (`if: github.event_name == 'pull_request'`): checkout@v7, `gradle/actions/wrapper-validation@v6`, `actions/setup-java@v6` (temurin 21), `gradle/actions/setup-gradle@v6`, accept SDK licenses, `./gradlew :app:assembleDebug --stacktrace`
- [X] T017 [US2] In `.github/workflows/android-sign.yml` add job `sign` (`if: github.event_name != 'pull_request'`) with the same setup steps, then a "Check signing secrets" step that receives the four secrets via `env:` and emits `::error::Missing secret NAME` for each empty one and exits 1; "Decode signing keystore" and "Write keystore.properties" steps identical to the whiskergrid reference (paths under `android/app/`)
- [X] T018 [US2] In the `sign` job add "Build signed release APK" (`./gradlew :app:assembleRelease -PappVersionCode=${{ github.run_number }} --stacktrace`), then "Verify signature": locate the newest `$ANDROID_HOME/build-tools/*/apksigner`, run `verify --print-certs`, fail unless exactly one `Signer #` line, extract the `SHA-256 digest`, convert to upper-case colon-separated pairs, copy the APK to `keedroidsign-sample.apk`, write `signing-report.json` (`request_id`, `commit`, `apk`, `signer_count`, `sha256`) and append a Markdown table to `$GITHUB_STEP_SUMMARY`
- [X] T019 [US2] In the `sign` job add `actions/upload-artifact@v7` steps for `sample-apk` (`android/keedroidsign-sample.apk`) and `signing-report` (`android/signing-report.json`), and a final `if: always()` step "Clean up signing files" removing `app/release.keystore.jks` and `app/keystore.properties`
- [X] T020 [US2] Lint the workflow with `actionlint` if available (otherwise a YAML parse check with Python `yaml.safe_load`) and fix findings

**Checkpoint**: US2 complete locally; the remote run is validated after merge (research R7)

---

## Phase 5: User Story 3 - Live test: export secrets and verify the signed APK (Priority: P2)

**Goal**: An opt-in test proves key generation → secret export → CI signing end-to-end against this repository

**Independent Test**: Without `KDS_LIVE_GITHUB_TOKEN` the test is skipped; with it (after merge) the test passes when the workflow's reported fingerprint equals the generated key's (quickstart §5)

**Depends on**: US2 workflow file (by name `android-sign.yml`); feature-001 core library

### Implementation for User Story 3

- [X] T021 [US3] Create `tests/KeeDroidSign.LiveTests/KeeDroidSign.LiveTests.csproj` (net48, `IsTestProject`, xUnit 2.9.x, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, reference `System.Net.Http`, `System.IO.Compression`, `System.Runtime.Serialization`, project reference `../../src/KeeDroidSign.Core`) and add it to `KeeDroidSign.sln`
- [X] T022 [P] [US3] Create `tests/KeeDroidSign.LiveTests/LiveSettings.cs`: reads `KDS_LIVE_GITHUB_TOKEN`, `KDS_LIVE_REPOSITORY` (default `kolod/kee-droid-sign`), `KDS_LIVE_REF` (default `main`), `KDS_LIVE_TIMEOUT_MINUTES` (default 20); `ToString()` never includes the token
- [X] T023 [P] [US3] Create `tests/KeeDroidSign.LiveTests/LiveFactAttribute.cs`: xUnit `FactAttribute` that sets `Skip = "Live test disabled: set KDS_LIVE_GITHUB_TOKEN to run it"` when the token is absent
- [X] T024 [US3] Create `tests/KeeDroidSign.LiveTests/GitHubActionsHelper.cs` (test-only, `IDisposable`) per [contracts/live-test.md](./contracts/live-test.md): own `HttpClient` with `AllowAutoRedirect = false`, Bearer token, `Accept: application/vnd.github+json`, `User-Agent: KeeDroidSign-LiveTests`; `DispatchAsync(repo, workflowFile, ref, requestId)` with header `X-GitHub-Api-Version: 2026-03-10` returning `(RunId, HtmlUrl)` from the 200 body; `GetRunAsync(repo, runId)` returning `status`, `conclusion`, `display_title`; `WaitForCompletionAsync(...)` polling every 15 s until completed or timeout; `DownloadSigningReportAsync(repo, runId)` listing run artifacts, requesting `/actions/artifacts/{id}/zip`, following the `Location` header with a separate token-less `HttpClient`, unzipping `signing-report.json` in memory; DataContract DTOs; error messages contain status codes and URLs only
- [X] T025 [US3] Create `tests/KeeDroidSign.LiveTests/SigningEndToEndTests.cs` with `[LiveFact]` test `ExportedSecrets_SignTheSampleApk`: generate passwords (`PasswordGenerator`) and an RSA 2048 keystore (`KeystoreGenerator`, alias `e2e`), compute the expected SHA-256 fingerprint; export via `SecretExporter.ExportAsync(..., new SecretMapping(), overwrite: true, ...)` and assert `Succeeded`; dispatch with a new GUID request ID; wait; assert conclusion `success` (failure message includes `HtmlUrl`), `display_title` contains the request ID, report `signer_count == 1`, `sha256` equals expected and `request_id` matches; write only run URL, request ID and fingerprints to `ITestOutputHelper`
- [X] T026 [US3] Wrap all failure paths in `SigningEndToEndTests.cs` so any exception/assert message is checked with a local leak check against the token, both passwords and the keystore Base64 before being surfaced (FR-015)
- [X] T027 [US3] Build the solution and run `dotnet test tests/KeeDroidSign.LiveTests -c Release` without the token: exactly one test, reported as skipped with the documented reason; run `dotnet test tests/KeeDroidSign.Core.Tests -c Release`: still 172 passed, offline

**Checkpoint**: US3 implemented; the live run itself happens after merge (research R7)

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T028 [P] Update `README.md`: repository layout gains `android/` and `tests/KeeDroidSign.LiveTests/`; new section "End-to-end signing test" with the workflow name, the token permissions (Actions RW, Secrets RW, Metadata R / classic `repo`+`workflow`), the env variables, and the note that each live run replaces the repository's signing secrets with a throwaway key
- [X] T029 [P] Update root `.gitignore` to also ignore `android/**/keystore.properties` and `android/**/local.properties` (defense in depth next to `android/.gitignore`)
- [X] T030 Review against the constitution: all files English, nothing under `keepass/` changed, no keystore/properties/local.properties tracked (`git ls-files android | grep -E "jks|keystore|local.properties"` empty), Android fixture not referenced by `KeeDroidSign.sln`
- [X] T031 Execute quickstart §1–3 and §5 (skip path) locally and record results; §4 and the live path of §5 run after merging into `main`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** → **US1** → **US2** → **US3** → **Polish**
- US3's code (T021–T026) does not need US2 to be finished and can be written in parallel with US2;
  only its live execution needs the workflow on `main`.

### Story Graph

```text
Setup → US1 ─→ US2 ─┐
          └──────── US3 (code) ─→ Polish ─→ merge to main ─→ live run (quickstart §4–5)
```

### Within Each Story

Configuration/resources → code → local verification.

## Parallel Opportunities

- Phase 1: T002 ‖ T003 ‖ T004 ‖ T005 after T001
- US1: T007 ‖ T008 ‖ T009, then T010
- US3: T022 ‖ T023 after T021; whole US3 code in parallel with US2 (different directories)
- Polish: T028 ‖ T029

### Parallel Example: User Story 1

```text
Task: "Create strings.xml in android/app/src/main/res/values/strings.xml"
Task: "Create AndroidManifest.xml in android/app/src/main/AndroidManifest.xml"
Task: "Create MainActivity.kt in android/app/src/main/kotlin/io/github/kolod/keedroidsign/sample/MainActivity.kt"
```

### Parallel Example: US2 and US3

```text
Agent A: T012–T020 (android/app/build.gradle.kts, .github/workflows/android-sign.yml)
Agent B: T021–T026 (tests/KeeDroidSign.LiveTests/*)
```

## Implementation Strategy

### MVP First

1. Phase 1 + US1 → a buildable APK for tests (the user's first goal)
2. US2 → signed builds locally and the workflow
3. US3 → live end-to-end test

### Delivery

1. PR from `002-android-signing-e2e` to `main` (PR CI runs the `build-debug` job)
2. After merge: run the workflow manually (quickstart §4), then the live test (quickstart §5)

## Notes

- Never commit `keystore.properties`, `local.properties`, `*.jks`, or tokens
- Throwaway keys only; each live run overwrites this repository's signing secrets
- Commit after each phase
- Found during T014: the feature-001 core encoded certificate subjects most-specific-first
  (CN ... C), so Android tools displayed `C=UA, O=Acme, CN=...`. Fixed in
  `src/KeeDroidSign.Core/Keystore/DistinguishedName.cs` to encode C ... CN like keytool, with the
  regression test `ToX509Name_EncodesMostGeneralAttributeFirst_LikeKeytool`.
