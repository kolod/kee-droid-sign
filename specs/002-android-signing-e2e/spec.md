# Feature Specification: Android Signing End-to-End Test

**Feature Branch**: `002-android-signing-e2e`

**Created**: 2026-10-08

**Status**: Draft

**Input**: User description: "Create a hello world Android app in Kotlin so that we have an APK file
for tests, and a GitHub Actions workflow that builds and signs it. Add a test that verifies adding
secrets directly to our own repository. https://github.com/kolod/whiskergrid.git can be used as an
example of how the app is built and signed." (original request was written in Ukrainian;
translated per Constitution Principle I)

## Scope Note

Feature 001 proved each core module in isolation, with GitHub simulated. This feature closes the
loop against the real world: a minimal Android app lives in this repository, a CI workflow signs it
with whatever key is stored in the repository's secrets, and a test uses the plugin core to put a
freshly generated key into those secrets. If the signed app carries exactly that key, the whole
chain — key generation → secret export → CI signing — is proven to work.

The reference project `kolod/whiskergrid` shows the signing pattern to follow: the workflow decodes
`ANDROID_KEYSTORE_BASE64` into a keystore file and writes `storePassword`, `keyAlias`,
`keyPassword` into `keystore.properties`, which the build reads.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Minimal test Android app (Priority: P1)

The maintainer needs a tiny, real Android application in the repository that can be built into an
installable APK. It exists only to be signed in tests; it shows a single "Hello, World!" screen.

**Why this priority**: Without a buildable app there is nothing to sign; every other story depends
on it.

**Independent Test**: Build the app locally without any signing secrets; an APK is produced and
installs/launches on an emulator or device showing "Hello, World!".

**Acceptance Scenarios**:

1. **Given** a fresh clone, **When** the maintainer builds the app with the standard build command,
   **Then** a debug APK is produced without any secrets or extra setup beyond the Android SDK.
2. **Given** the APK is installed on a device, **When** it is launched, **Then** a single screen with
   the text "Hello, World!" is shown.
3. **Given** the app source, **When** it is reviewed, **Then** it contains no features, permissions or
   network access beyond what displaying the text requires.

---

### User Story 2 - CI workflow builds and signs the APK (Priority: P1)

A GitHub Actions workflow builds a release APK of the test app and signs it with the key stored in
the repository secrets (`ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`,
`ANDROID_KEY_PASSWORD`), exactly as the reference project does. The signed APK and the SHA-256
fingerprint of its signing certificate are published as the run's outputs.

**Why this priority**: This is the consumer side of the secrets the plugin exports; it defines what
"the export worked" means.

**Independent Test**: Put any valid keystore into the four secrets manually, run the workflow, and
check that the produced APK verifies and is signed by that keystore's certificate.

**Acceptance Scenarios**:

1. **Given** the four secrets contain a valid keystore, **When** the workflow runs, **Then** it
   produces a signed release APK as a downloadable artifact and reports the signing certificate's
   SHA-256 fingerprint in the run summary.
2. **Given** any of the four secrets is missing or wrong (bad Base64, wrong password, wrong alias),
   **When** the workflow runs, **Then** it fails with a clear message and does **not** fall back to
   a debug key.
3. **Given** the workflow finishes (success or failure), **When** the run ends, **Then** the decoded
   keystore and the properties file containing passwords have been deleted from the runner.
4. **Given** a pull request from a fork (no access to secrets), **When** the workflow is triggered,
   **Then** the signing job is skipped with an explanatory message instead of failing or signing
   with another key.
5. **Given** the produced APK, **When** its signature is verified, **Then** verification succeeds
   and exactly one signer is present.

---

### User Story 3 - Live test: export secrets to this repository and verify the signed APK (Priority: P2)

The maintainer runs an opt-in test that uses the plugin core to generate a fresh signing key, export
it to this repository's Actions secrets via the real GitHub API, trigger the signing workflow, wait
for it, and confirm the APK was signed with exactly the generated key.

**Why this priority**: It is the only check that proves the plugin's export works against real
GitHub and a real build; it requires a token and network, so it is opt-in rather than part of the
default offline suite.

**Independent Test**: Run the test with a GitHub token configured; it passes only if the
fingerprint reported by the workflow equals the fingerprint of the key the test just generated.

**Acceptance Scenarios**:

1. **Given** a token with permission to write secrets and run workflows on this repository, **When**
   the live test runs, **Then** it generates a new key, writes the four secrets (overwriting existing
   ones), triggers the workflow, waits for completion, and passes only if the reported certificate
   fingerprint matches the generated key.
2. **Given** no token is configured, **When** the test suite runs, **Then** the live test is reported
   as skipped with a reason, and the rest of the suite is unaffected.
3. **Given** the workflow fails or does not finish within the time limit, **When** the live test
   waits, **Then** the test fails with a message that includes a link to the workflow run.
4. **Given** the live test runs, **When** it reports progress or failure, **Then** no password, token
   or keystore content appears in its output.

### Edge Cases

- Two live test runs at the same time overwrite each other's secrets; the test must identify its own
  workflow run and must not pass on a run that used the other key.
- The workflow is triggered while the secrets are still being written; the run must use the complete
  new set (secrets are written before the workflow is triggered).
- The repository's existing signing secrets are replaced by a throwaway test key after a live run;
  this is acceptable only because this repository has no production app.
- The workflow runner's Android SDK or JDK versions change; the build must pin the versions it needs.
- An APK signed with an older key may remain as an artifact from previous runs; the test must check
  the artifact or summary of its own run only.

## Requirements *(mandatory)*

### Functional Requirements

**Test app**

- **FR-001**: The repository MUST contain a minimal Android application written in Kotlin that shows
  a single "Hello, World!" screen.
- **FR-002**: The app MUST build into a debug APK locally without any signing secrets.
- **FR-003**: The app MUST request no runtime permissions and MUST NOT access the network.
- **FR-004**: The release build MUST read signing settings from a `keystore.properties` file
  (`storeFile`, `storePassword`, `keyAlias`, `keyPassword`), following the reference project;
  both that file and the keystore MUST be excluded from version control.
- **FR-005**: Unlike the reference project, a release build without signing settings MUST fail
  instead of silently signing with the debug key.

**CI workflow**

- **FR-006**: A GitHub Actions workflow MUST build the release APK and sign it using the four
  repository secrets defined by feature 001 (`ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`,
  `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`).
- **FR-007**: The workflow MUST be startable manually and MUST also run on pushes to `main` that
  change the test app or the workflow itself.
- **FR-008**: The workflow MUST fail early with a clear message when any secret is missing, and MUST
  skip signing (with a notice) when secrets are unavailable to the run, such as pull requests from
  forks.
- **FR-009**: The workflow MUST verify the APK signature after building and MUST publish the signed
  APK as a run artifact and the signer certificate's SHA-256 fingerprint (format `AA:BB:...`, same as
  feature 001) in the run summary.
- **FR-010**: The workflow MUST delete the decoded keystore and the properties file in all cases,
  including failures.
- **FR-011**: The workflow MUST accept an optional run identifier input and echo it in the run name
  or summary, so that a caller can find the exact run it triggered.

**Live test**

- **FR-012**: A live test MUST generate a new signing key with the plugin core, export it to this
  repository's secrets with the plugin core (overwrite confirmed), trigger the workflow with a unique
  run identifier, wait for that run, and compare the reported fingerprint with the generated key's
  fingerprint.
- **FR-013**: The live test MUST be opt-in: it runs only when a GitHub token is provided through an
  environment variable and is otherwise reported as skipped with a reason.
- **FR-014**: The live test MUST time out after a bounded wait (default 20 minutes) and then fail
  with a link to the run.
- **FR-015**: The live test MUST NOT print or log passwords, the token, or keystore content.
- **FR-016**: The target repository for the live test MUST default to this project's repository
  (`kolod/kee-droid-sign`) and MAY be overridden through an environment variable.

### Key Entities

- **Test App**: Minimal Android application used only as a signing target; identified by its
  application ID.
- **Signing Workflow Run**: One execution of the CI workflow; has a run identifier supplied by the
  caller, a status, a signed APK artifact and a reported certificate fingerprint.
- **Live Test Run**: One execution of the opt-in test; owns a freshly generated keystore and a unique
  run identifier used to match its workflow run.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A fresh clone produces an installable debug APK of the test app with a single build
  command and no secrets.
- **SC-002**: With valid secrets, the workflow produces a signed APK whose signing certificate
  fingerprint equals the fingerprint of the keystore in the secrets in 100% of runs.
- **SC-003**: With a missing or invalid secret, 100% of workflow runs fail or skip with an
  explanatory message; none produce an APK signed with any other key.
- **SC-004**: The live test completes (export + build + verification) in under 20 minutes on a
  normal connection.
- **SC-005**: After any workflow run, no keystore or password file remains on the runner, and no
  secret value appears in any log, summary or test output.

## Assumptions

- The test app lives in the top-level `android/` folder, separate from the .NET solution, and is
  written in Kotlin, as required for Android test fixtures by the constitution (v1.3.0, Technology
  & Platform Constraints). It is never distributed with the plugin.
- Build tool and SDK versions follow the reference project unless a newer stable version is
  required; exact versions are a planning decision.
- Each live test run generates a throwaway key and overwrites this repository's four signing
  secrets. This repository has no production app, so no real signing key is at risk.
- The live test is not part of the default CI test run; it is started manually by the maintainer.
  A token with *Secrets: read and write* and *Actions: read and write* (fine-grained) or `repo` +
  `workflow` scopes (classic) is required.
- The signed APK is published only as a workflow artifact, not as a GitHub Release.
- The application ID uses the maintainer's namespace (e.g. `io.github.kolod.keedroidsign.sample`).
