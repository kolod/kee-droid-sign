# Tasks: Export Signing Secrets to a GitHub Environment

**Input**: Design documents from `/specs/005-environment-secrets/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/github-api.md](./contracts/github-api.md),
[contracts/plugin-ui.md](./contracts/plugin-ui.md), [quickstart.md](./quickstart.md)

**Tests**: Requested — every story's "Independent Test" uses a simulated GitHub, and Constitution
Principle VI requires the core GitHub logic to be unit-tested. Core tests use
`tests/KeeDroidSign.Core.Tests/Support/FakeGitHubHandler.cs`; plugin tests use
`tests/KeeDroidSign.Tests/Support/TestDatabase.cs`.

**Organization**: Tasks are grouped by user story (US1 P1, US2 P2, US4 P2, US3 P3).

**Conventions**: Core (`src/KeeDroidSign.Core`) is modern C#; plugin sources (`src/KeeDroidSign`)
must stay **C# 5** (no `?.`, `nameof`, expression-bodied members, string interpolation, tuples,
`out var`). New UI strings go to `src/KeeDroidSign/Properties/Strings.resx` via
`build/Update-Strings.ps1`. Error texts are fixed English and never include tokens, secret values or
response bodies.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User story the task belongs to

---

## Phase 1: Setup

- [X] T001 Bump the version to 1.1.0: `<Version>1.1.0</Version>` in `Directory.Build.props` and `AssemblyVersion`/`AssemblyFileVersion` `1.1.0.0` in `src/KeeDroidSign/Properties/AssemblyInfo.cs` (the existing version-consistency test in `tests/KeeDroidSign.Tests/UI/PluginSmokeTests.cs` must still pass)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Scope-aware GitHub client and the fake GitHub that all stories use

- [X] T002 [P] Create `src/KeeDroidSign.Core/GitHub/EnvironmentNames.cs`: `public static class EnvironmentNames` with `IsValid(string)` and `Validate(string)` (throws `ArgumentException`): 1–255 chars, no `/`, no control characters, no leading/trailing whitespace (research R5)
- [X] T003 [P] Create `src/KeeDroidSign.Core/GitHub/SecretScope.cs`: immutable `SecretScope(RepositoryTarget repository, string environment = null)` with `Repository`, `Environment` (null = repository level, otherwise validated by `EnvironmentNames.Validate`), `IsEnvironment`, internal `SecretsPath` (`repos/{o}/{r}/actions/secrets` or `repos/{o}/{r}/environments/{escaped env}/secrets`, all segments via `Uri.EscapeDataString`, reuse `GitHubClient.RepoPath`), `ToString()` → `owner/name` or `owner/name, environment {env}`, static `ForRepository(RepositoryTarget)`
- [X] T004 [P] Create `src/KeeDroidSign.Core/GitHub/EnvironmentModels.cs`: enums `EnvironmentExistence { Found, NotFound, NoPermission }`, `BranchPolicyKind { AllBranches, ProtectedBranches, CustomPolicies, Unknown }`; class `EnvironmentStatus(existence, policy)`; `EnvironmentNotFoundException : GitHubApiException` (message names repository and environment, says it may also be invisible to the token); DTOs (`[DataContract]`, internal) for `GET /environments/{env}` (`deployment_branch_policy` {`protected_branches`, `custom_branch_policies`} nullable object, `protection_rules[]` with `type`, `wait_timer`, `prevent_self_review`, `reviewers[]` {`type`, `reviewer.id`}), the environment PUT body, and branch policies list/create (`name`, `type`)
- [X] T005 Refactor `src/KeeDroidSign.Core/GitHub/GitHubClient.cs`: add `ListSecretNamesAsync(SecretScope, ct)`, `GetPublicKeyAsync(SecretScope, ct)` and `PutSecretAsync(SecretScope, name, encryptedValue, keyId, ct)` using `scope.SecretsPath`; keep the existing `RepositoryTarget` overloads as one-line delegates to `SecretScope.ForRepository(repo)`; in environment scope `GetPublicKeyAsync` maps 404 → `EnvironmentNotFoundException` and 403 → `GitHubApiException` "The token may not access environment secrets (needs the Environments permission)."; scope-specific 403/404 text in `PutSecretAsync` ("…write environment secrets." vs "…write repository secrets.") (depends on T003, T004)
- [X] T006 Create partial `src/KeeDroidSign.Core/GitHub/GitHubClient.Environments.cs` (make `GitHubClient` `partial`): `GetEnvironmentAsync(repo, env, ct)` → `EnvironmentInfo` (status 200 → policy mapped per research R3 + existing `wait_timer`, `prevent_self_review`, reviewers; 404 → NotFound; 403 → NoPermission; network/rate limit → `GitHubApiException`), `PutEnvironmentAsync(repo, env, EnvironmentPutDto, ct)` → status code, `ListBranchPoliciesAsync(repo, env, ct)` → list of (name, type), `AddBranchPolicyAsync(repo, env, name, type, ct)` → status code (200/303 ok), `DeleteSecretAsync(RepositoryTarget, name, ct)` → `SecretWriteResult`-like outcome (204/404 → deleted, 403 → failed "Insufficient permissions to delete repository secrets.") (depends on T004, T005)
- [X] T007 Extend `tests/KeeDroidSign.Core.Tests/Support/FakeGitHubHandler.cs`: environments dictionary (`AddEnvironment(name, policy, waitTimer, reviewers)` with secrets, own public key pair, branch policies), handlers for every endpoint in contracts/github-api.md, forced status per path prefix (e.g. 403 for `environments/` or for `actions/secrets`), and a request log (method + path) for assertions; keep existing behaviour for repository secrets unchanged
- [X] T008 [P] Tests `tests/KeeDroidSign.Core.Tests/GitHub/SecretScopeTests.cs`: valid/invalid environment names (empty, `/`, 256 chars, control char, surrounding spaces), paths for both scopes incl. escaping (`my env` → `my%20env`), `ToString()` texts (depends on T002, T003)
- [X] T009 [P] Tests `tests/KeeDroidSign.Core.Tests/GitHub/GitHubClientEnvironmentTests.cs`: environment public key/list/put hit the environment paths; 404 public key → `EnvironmentNotFoundException`; 403 → permission message; `GetEnvironmentAsync` maps `null`/protected/custom policies and 403 → NoPermission; `DeleteSecretAsync` 204/404/403; no message contains the token (use `SecretLeakScanner`) (depends on T005–T007)

**Checkpoint**: `dotnet test tests/KeeDroidSign.Core.Tests` green; repository-scope tests unchanged

---

## Phase 3: User Story 1 - Export into a configured environment (Priority: P1) 🎯 MVP

**Goal**: With a default or per-app environment, the four secrets go into that environment only;
confirmation names repository and environment; missing environment/permission stop before writing.

**Independent Test**: Simulated GitHub, setting `release`: all requests go to environment endpoints
with the environment key; no repository-level PUT; empty setting behaves as 1.0.1.

### Tests for User Story 1

- [X] T010 [P] [US1] Tests in `tests/KeeDroidSign.Core.Tests/GitHub/SecretExporterTests.cs`: environment scope plan/export writes 4 secrets to the environment, encrypted with the environment key (decrypt with the fake's private key), zero repository PUTs (request log); missing environment → `EnvironmentNotFoundException` and zero PUTs; 403 on environment → permission error and zero PUTs; plan includes `EnvironmentStatus` (policy Unknown when `GET /environments/{env}` is 403); repository scope unchanged
- [X] T011 [P] [US1] Tests `tests/KeeDroidSign.Tests/Settings/PluginSettingsEnvironmentTests.cs`: empty store → `DefaultEnvironment == "release"`; store with `RootGroup` but no `GitHubEnvironment` → empty; saved value round-trips (incl. explicit empty); `Validate()` rejects invalid names with a message naming the field
- [X] T012 [P] [US1] Tests `tests/KeeDroidSign.Tests/Storage/ExportTargetTests.cs`: parse `DroidSign.ExportTarget` absent/`repository`/`environment:staging`/garbage (garbage → Default + warning); `ExportTarget.Resolve` uses override first, then default; `FromApp` flag; `DroidSignStore.SetExportTarget` writes/removes the field, touches the entry and marks the database modified
- [X] T013 [P] [US1] Tests in `tests/KeeDroidSign.Tests/Services/ExportConfirmationTests.cs` and `tests/KeeDroidSign.Tests/Services/ExportServiceTests.cs`: confirmation names `owner/repo, environment release`; repository-level text unchanged; `ExportService` uses the per-app override over the global setting, and `repository` override exports at repository level (acceptance scenario 7)

### Implementation for User Story 1

- [X] T014 [US1] Extend `src/KeeDroidSign.Core/GitHub/SecretExporter.cs` (+ `ExportPlan` there): `PlanAsync(SecretScope, mapping, ct)` and `ExportAsync(SecretScope, keystore, mapping, overwrite, ct)`; environment scope: public key first (R2, throws on missing/forbidden), then `GetEnvironmentAsync` for the policy (NoPermission → `Unknown`), then list environment secrets; `ExportPlan` gains `Scope` and `Environment` (null in repository scope); existing `RepositoryTarget` overloads delegate to repository scope (depends on T005, T006)
- [X] T015 [P] [US1] Add `DefaultEnvironment` to `src/KeeDroidSign/Settings/PluginSettings.cs`: config key `GitHubEnvironment`; `Load`: missing key → `store.Get("RootGroup") == null ? "release" : ""` (research R7); `Save` writes it; `Validate` uses `EnvironmentNames` when non-empty ("The GitHub environment name …"); C# 5
- [X] T016 [P] [US1] Add `ExportTarget = "DroidSign.ExportTarget"` to `src/KeeDroidSign/Storage/EntryFields.cs`; add `ExportTargetKind`, `ExportTargetOverride` (Parse/ToFieldValue, invalid → Default + warning) and `ExportTarget` (`Repository`, `Environment`, `FromApp`, `ToScope()`, `ToString()`, static `Resolve(AppKeystore, PluginSettings)`) to `src/KeeDroidSign/Storage/Models.cs`; `AppKeystore.TargetOverride` reads the keystore entry field; C# 5
- [X] T017 [US1] Add `SetExportTarget(AppKeystore app, ExportTargetOverride value)` to `src/KeeDroidSign/Storage/DroidSignStore.Write.cs` (Default removes the field; otherwise `ProtectedString(false, value)`; `entry.Touch(true)`; `_database.Modified = true`); add an unknown-value warning to `BuildApp` in `src/KeeDroidSign/Storage/DroidSignStore.cs` (depends on T016)
- [X] T018 [US1] Update `src/KeeDroidSign/Services/ExportService.cs`: `ResolveTarget(KeyContext)` → `ExportTarget.Resolve(key.App, _settings())`; `PlanAsync`/`ExportAsync` pass `target.ToScope()` (depends on T014, T016)
- [X] T019 [US1] Update `src/KeeDroidSign/Services/ExportConfirmation.cs`: `Build(ExportTarget target, ExportPlan plan)` — first line `ConfirmExportEnvironment` ("Export the signing secrets to {0}, environment {1}?") or the existing `ConfirmExport`; keep create/overwrite sections; add `BuildEnvironmentMissing(ExportTarget)` with the manual steps from contracts/plugin-ui.md (depends on T016)
- [X] T020 [US1] Add strings (English) via `build/Update-Strings.ps1` to `src/KeeDroidSign/Properties/Strings.resx` and regenerate `Strings.cs`: `LabelGitHubEnvironment`, `GitHubEnvironmentHint`, `LabelExportTo`, `ButtonChangeTarget`, `ExportTargetDefaultFmt`, `ExportTargetAppFmt`, `ExportTargetRepositoryDefault`, `ExportTargetRepositoryApp`, `ConfirmExportEnvironment`, `ErrorEnvironmentMissing`, `EnvironmentManualSteps`, `ErrorEnvironmentPermission`, `ExportTargetFormTitle`, `RadioUseDefault`, `RadioRepositoryLevel`, `RadioEnvironment`, `WarningUnknownExportTarget`
- [X] T021 [US1] Add the **GitHub environment** row with hint to `src/KeeDroidSign/UI/OptionsTabControl.cs` (load/build into `PluginSettings.DefaultEnvironment`, invalid → existing `TryBuild` error path) (depends on T015, T020)
- [X] T022 [US1] Create `src/KeeDroidSign/UI/ExportTargetForm.cs` (C# 5, `FormLayout`): radio *Use the default ({default})*, *Repository level*, *Environment:* + text box (enabled only with its radio, validated by `EnvironmentNames`), OK/Cancel; property `Result` (`ExportTargetOverride`) (depends on T016, T020)
- [X] T023 [US1] Update `src/KeeDroidSign/UI/EntryTabControl.cs`: "Export to" row (target text + **Change…**) after the repository row; **Change…** shows `ExportTargetForm`, calls `DroidSignStore.SetExportTarget`, invokes a new `Action<PwDatabase> databaseChanged` constructor callback and reloads the key; `OnExport` uses `ExportConfirmation.Build(target, plan)` and shows `EnvironmentNotFoundException` as `BuildEnvironmentMissing` text; status texts name the target (depends on T018, T019, T022)
- [X] T024 [US1] Pass the callback from `src/KeeDroidSign/KeeDroidSignExt.cs` (`CreateEntryTab`): refresh the main window and save when `SaveAfterKeyChange` (reuse the logic of `ShowInDatabase` without selecting another entry) (depends on T023)

**Checkpoint**: US1 tests green; manual quickstart Q2 steps 1, 2, 4, 6 work in KeePass

---

## Phase 4: User Story 2 - Leave no readable copy at repository level (Priority: P2)

**Goal**: After a fully successful environment export, offer to delete repository-level copies.

**Independent Test**: Simulated repository copies → Yes deletes exactly those; No keeps them with a
warning; failed export → no question, no DELETE.

### Tests for User Story 2

- [X] T025 [P] [US2] Tests in `tests/KeeDroidSign.Core.Tests/GitHub/SecretExporterTests.cs`: `FindRepositoryCopiesAsync` returns only the configured names present at repository level; 403 on listing → `Checked == false`; `DeleteRepositorySecretsAsync` → `Deleted` per name, 404 counts as deleted, 403 → `Failed`; request log shows no DELETE in an export that had a failed write
- [X] T026 [P] [US2] Tests in `tests/KeeDroidSign.Tests/Services/ExportConfirmationTests.cs` and `tests/KeeDroidSign.Tests/Services/ExportServiceTests.cs`: cleanup question lists the names, repository and environment; `ExportService.FindRepositoryCopiesAsync` returns nothing in repository scope

### Implementation for User Story 2

- [X] T027 [US2] Add `SecretOutcomeStatus.Deleted`, `RepositoryCopies` (names, `Checked`), `FindRepositoryCopiesAsync(repo, mapping, ct)` and `DeleteRepositorySecretsAsync(repo, names, ct)` to `src/KeeDroidSign.Core/GitHub/SecretExporter.cs` (depends on T006, T014)
- [X] T028 [US2] Add `FindRepositoryCopiesAsync(KeyContext, token, ct)` (empty result in repository scope) and `DeleteRepositoryCopiesAsync(KeyContext, token, names, ct)` to `src/KeeDroidSign/Services/ExportService.cs`; add `BuildCleanup(ExportTarget, IList<string>)` to `src/KeeDroidSign/Services/ExportConfirmation.cs` (depends on T027)
- [X] T029 [US2] Add strings `ConfirmCleanup`, `WarningRepositoryCopiesKept`, `WarningRepositoryCopiesUnchecked` via `build/Update-Strings.ps1` (`src/KeeDroidSign/Properties/Strings.resx`, `Strings.cs`)
- [X] T030 [US2] In `src/KeeDroidSign/UI/EntryTabControl.cs` `OnExport`: only when `result.Succeeded` and the target is an environment → find copies; if any, `MessageService.AskYesNo(BuildCleanup…)`; Yes → delete and append outcomes to the results box; No → warning label; `Checked == false` → unchecked note (depends on T028, T029)

**Checkpoint**: quickstart Q2 step 5 works

---

## Phase 5: User Story 4 - This project's signing workflow and live test use the environment (Priority: P2)

**Goal**: Sample workflow reads environment `release`; live test exports there; README explains it.

**Independent Test**: Live test passes against `release`; a run from another branch gets no secrets.

- [X] T031 [P] [US4] Update `.github/workflows/android-sign.yml`: `environment: release` on job `sign`; header comment and the "Check signing secrets" error text mention environment `release`; `build-debug` unchanged; run `actionlint` if available
- [X] T032 [P] [US4] Update `tests/KeeDroidSign.LiveTests/LiveSettings.cs` (`KDS_LIVE_ENVIRONMENT`, default `release`, `-` = repository level, included in `ToString()`) and `tests/KeeDroidSign.LiveTests/SigningEndToEndTests.cs` (export with `new SecretScope(repo, env)`, output the scope, class comment)
- [X] T033 [US4] Update `README.md`: usage (Export to row, Change…, Protect environment…), new section "GitHub environment" (why, create with deployment branches/tags `main` + `v*`, optional reviewers, private-repo plan note), token permission table (+ Environments RW for export, Actions R for the protection check, Secrets RW only for repository level/cleanup, Administration RW only for Protect environment), GitHub Actions contract (`environment: release` on the signing job, secrets only from `main`/`v*`, unsigned builds for PRs), live-test table (`KDS_LIVE_ENVIRONMENT`, token permissions), "Protecting the signing key in CI" updated

**Checkpoint**: Static changes ready; live verification happens in Phase 7 (maintainer steps)

---

## Phase 6: User Story 3 - Warn about, and optionally protect, the environment (Priority: P3)

**Goal**: Warning for unrestricted environments; explicit **Protect environment…** action.

**Independent Test**: Simulated environment without policy → warning; protect action with/without
Administration → created + policies / manual steps, existing reviewers and wait timer kept.

### Tests for User Story 3

- [X] T034 [P] [US3] Tests `tests/KeeDroidSign.Core.Tests/GitHub/EnvironmentProtectorTests.cs`: missing env → PUT with custom policies + POST `main` (branch) and `v*` (tag); `null` policy with reviewers and wait timer → PUT body keeps them; protected-branches policy → no write, `AlreadyRestricted`; custom with `main` present → only `v*` added; 303 counts as ok; 403 on PUT/POST → `NeedsAdministration`, no further calls; 403 on GET → `NeedsActionsRead`
- [X] T035 [P] [US3] Tests in `tests/KeeDroidSign.Tests/Services/ExportConfirmationTests.cs`: `AllBranches` → warning text present; `ProtectedBranches`/`CustomPolicies` → absent; `Unknown` → note present

### Implementation for User Story 3

- [X] T036 [US3] Create `src/KeeDroidSign.Core/GitHub/EnvironmentProtector.cs` with `ProtectionResult`/`ProtectionStatus` (in `EnvironmentModels.cs`) implementing research R4 (depends on T006)
- [X] T037 [US3] Add `ProtectEnvironmentAsync(KeyContext, token, ct)` to `src/KeeDroidSign/Services/ExportService.cs` (throws `InvalidOperationException` when the target is repository level); add warning/note sections for `BranchPolicyKind.AllBranches`/`Unknown` to `ExportConfirmation.Build` and `BuildProtect(ExportTarget)` / `DescribeProtection(ProtectionResult, ExportTarget)` to `src/KeeDroidSign/Services/ExportConfirmation.cs` (depends on T036)
- [X] T038 [US3] Add strings `ButtonProtectEnvironment`, `ConfirmProtect`, `WarningEnvironmentUnrestricted`, `NoteEnvironmentUnchecked`, `ProtectDone`, `ProtectAlreadyRestricted`, `ProtectNeedsAdministration`, `ProtectNeedsActionsRead`, `StatusProtecting` via `build/Update-Strings.ps1`
- [X] T039 [US3] Add **Protect environment…** to `src/KeeDroidSign/UI/EntryTabControl.cs` next to **Change…** (enabled for environment targets with a token, no problems): confirm → `ProtectEnvironmentAsync` → status label with the result or the manual steps; same cancellation/error handling as export (depends on T037, T038)

**Checkpoint**: All automated tests green; quickstart Q2 step 3 works

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T040 Run `dotnet test KeeDroidSign.sln -c Release` and `powershell -File build/Build-Plgx.ps1` (C# 5 compile, product name, version 1.1.0.0); fix any failures
- [X] T041 Install with `build/Install-Plugin.ps1` and walk through quickstart Q2 in KeePass (user confirms the UI)
- [X] T042 Update `.specify/memory/constitution.md` only if a principle changed (expected: no change); mark tasks done in this file
- [ ] T043 Commit in logical GPG-signed commits (spec docs; core; plugin; workflow + live test + README), push branch, open PR to `main` (ask before merge)
- [ ] T044 Maintainer steps in `kolod/kee-droid-sign`, **each confirmed with the user first** (quickstart Q3): create/protect environment `release`; export a key into it; merge the PR; confirm the "Sign sample APK" run on `main` succeeds; run the live test; delete the four repository-level secrets; dispatch the workflow from a non-`main` branch and confirm the deployment is rejected
- [ ] T045 Offer release 1.1.0 (tag `v1.1.0` on `main` → release workflow); only after confirmation

---

## Phase 8: Export wizard rework (after review, 2026-10-08)

**Purpose**: Users' repositories differ (default branch name, protection). Replace the tab's target
row, Change… and Protect environment… buttons and the message boxes with an export wizard that
inspects the repository, proposes actions and shows results (spec Clarifications, research R11–R12).
Supersedes the UI parts of T019, T020, T022, T023, T030, T037–T039 (tab target row, `ExportTargetForm`,
message-box confirmations); their core and service parts remain.

- [X] T046 [P] Core: add repository inspection calls to `src/KeeDroidSign.Core/GitHub/GitHubClient.Environments.cs` (or a new partial `GitHubClient.Repository.cs`): default branch, active branch rule types, classic branch `protected`, tag rulesets with details (contracts/github-api.md "Repository inspection")
- [X] T047 Core: create `src/KeeDroidSign.Core/GitHub/RepositoryInspector.cs` and `RepositoryReport`/`RefProtection`/`DeploymentPattern` models (R11: protection rules, fnmatch include/exclude for `refs/tags/v*`, proposed patterns = default branch + `v*` minus existing; secrets plan; repository copies)
- [X] T048 Core: change `EnvironmentProtector.ProtectAsync` to take the patterns to add (remove the fixed `main`/`v*` list) in `src/KeeDroidSign.Core/GitHub/EnvironmentProtector.cs`
- [X] T049 [P] Core tests: `tests/KeeDroidSign.Core.Tests/GitHub/RepositoryInspectorTests.cs` (default branch `master` proposed, protected via ruleset / classic / unknown, tag creation restricted / not / unknown, environment missing → proposals and no secrets plan, existing patterns not proposed again, protected-branches mode → no proposals) and update `EnvironmentProtectorTests.cs` for explicit patterns
- [X] T050 Plugin: create `src/KeeDroidSign/UI/ExportWizardForm.cs` (C# 5; pages Target → Review → Results per contracts/plugin-ui.md; async inspection and run with cancellation; stores a changed target via `DroidSignStore.SetExportTarget`); add `ExportService.InspectAsync`; texts in `src/KeeDroidSign/Services/ExportConfirmation.cs` (renamed responsibilities: review/findings/results texts)
- [X] T051 Plugin: simplify `src/KeeDroidSign/UI/EntryTabControl.cs` back to the single **Export to GitHub** button that opens the wizard; remove `src/KeeDroidSign/UI/ExportTargetForm.cs`; update `KeeDroidSignExt.cs` callback; update strings via `build/Update-Strings.ps1` (remove unused ones)
- [X] T052 Plugin tests: update `tests/KeeDroidSign.Tests/Services/ExportConfirmationTests.cs` and `ExportServiceEnvironmentTests.cs` for findings/actions texts and `InspectAsync`
- [X] T053 README: wizard usage, default-branch/tag protection warnings and why they matter, Metadata/Contents permissions for the checks
- [X] T055 Core `RepositoryHardening` (tag ruleset, default-branch ruleset, remove `v*` pattern) + tests; wizard choices per warning ("Leave as is" default), service methods, strings (FR-006b, research R13)
- [X] T054 Run all tests, `Build-Plgx.ps1`, actionlint; user walks through the wizard in KeePass (quickstart Q2)

## Dependencies & Execution Order

- **Setup (T001)** → anytime.
- **Foundational (T002–T009)** blocks all stories (T005 → T006; T007 before T009).
- **US1 (T010–T024)** after Foundational — MVP.
- **US2 (T025–T030)** after US1 (uses the environment export flow and `EntryTabControl.OnExport`).
- **US4 (T031–T033)**: T031/T032 need only Foundational (`SecretScope`); T033 best after US1–US3 so
  the README describes the final UI.
- **US3 (T034–T039)** after US1 (confirmation and tab); independent of US2.
- **Polish (T040–T045)** last; T044 needs the PR merged and user confirmations.

### Within each story

Tests first (they should fail), then core → service → strings → UI.

## Parallel Opportunities

- Foundational: T002, T003, T004 together; then T008 alongside T006/T007.
- US1: T010–T013 together; T015 and T016 together.
- US2: T025 and T026 together.
- US4: T031 and T032 together (different files), in parallel with US1 implementation.
- US3: T034 and T035 together.

### Parallel example: User Story 1

```text
T010 SecretExporterTests (core)      T011 PluginSettingsEnvironmentTests
T012 ExportTargetTests               T013 ExportConfirmation/ExportService tests
then T015 PluginSettings  ‖  T016 EntryFields/Models
```

## Implementation Strategy

1. **MVP**: Setup + Foundational + US1 → export into an environment works and is tested; the
   default/override logic is complete.
2. **US2** removes the remaining exposure from earlier exports.
3. **US4** moves this repository onto the environment (workflow, live test, README).
4. **US3** adds the warning and the optional protect action.
5. Polish, PR, maintainer steps and release — each outward-facing step confirmed by the user.
