# Tasks: PLGX Release Publishing

**Input**: Design documents from `/specs/004-plgx-release/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/release-workflow.md](./contracts/release-workflow.md),
[quickstart.md](./quickstart.md)

**Tests**: No new test suites are requested. Verification is the existing test run inside the
release build, a new version-consistency test (research R6), script self-checks, actionlint, and the
1.0.0 release itself.

**Organization**: Tasks are grouped by user story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User story the task belongs to (US1–US2)

---

## Phase 1: Setup

**Purpose**: None needed — the workflow reuses existing scripts and projects.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Let the PLGX build carry an arbitrary release version (needed by US1 and US2)

- [X] T001 Add optional parameter `-Version` (validated `^\d+\.\d+\.\d+$`) to `build/Build-Plgx.ps1`: after staging, rewrite `[assembly: AssemblyVersion("…")]` and `[assembly: AssemblyFileVersion("…")]` in the **staged** `Properties/AssemblyInfo.cs` to `X.Y.Z.0` (throw if either attribute is not found); after the verification compile, throw unless `FileVersionInfo.FileVersion` of the compiled plugin equals `X.Y.Z.0` and print `Verified: version is X.Y.Z.0`; update the script's help text
- [X] T002 Verify locally with Windows PowerShell: `build/Build-Plgx.ps1 -Version 9.9.9` succeeds and prints the version line, `-Version 1.2` is rejected, running without `-Version` still works, and `git status` shows `src/KeeDroidSign/Properties/AssemblyInfo.cs` unchanged

**Checkpoint**: The PLGX can be stamped with any release version without touching the sources

---

## Phase 3: User Story 1 - Publish a release from a version tag (Priority: P1) 🎯 MVP

**Goal**: Pushing `vX.Y.Z[-pre]` on `main` publishes a GitHub Release with only `KeeDroidSign.plgx`

**Independent Test**: actionlint passes; after merge, a manual run with an invalid tag fails at validation and publishes nothing (quickstart §2)

### Implementation for User Story 1

- [X] T003 [US1] Create `.github/workflows/release.yml` (name `Release`, `run-name: Release ${{ inputs.tag || github.ref_name }}`) with triggers `push: tags: ['v*']` and `workflow_dispatch` inputs `tag` (string, required) and `replace_asset` (boolean, default false); top-level `permissions: contents: read`; `concurrency: group: release-${{ inputs.tag || github.ref_name }}`, `cancel-in-progress: false`
- [X] T004 [US1] In `.github/workflows/release.yml` add job `build` (`windows-latest`, `timeout-minutes: 30`, outputs `tag`, `version`, `prerelease`): step "Resolve tag" (pwsh) takes `inputs.tag` or `github.ref_name`, validates it with the regex from research R3, and writes `tag`, `version` (X.Y.Z) and `prerelease` (`true` if a suffix exists) to `$GITHUB_OUTPUT`; checkout `actions/checkout@v7` with `ref: refs/tags/<tag>`, `fetch-depth: 0`, `submodules: true`; step "Check branch" runs `git fetch origin main` and fails unless `git merge-base --is-ancestor HEAD origin/main`; step "Compare source version" emits `::warning::` when `Directory.Build.props` `<Version>` differs from the tag version
- [X] T005 [US1] Continue job `build` in `.github/workflows/release.yml`: `actions/setup-dotnet@v5` (10.0.x), `./build/Build-KeePassReference.ps1` (pwsh), `dotnet build KeeDroidSign.sln -c Release -p:Version=<version>`, `dotnet test KeeDroidSign.sln -c Release --no-build`, `./build/Build-Plgx.ps1 -Version <version>` (shell `powershell`), `actions/upload-artifact@v7` name `release-plgx` path `artifacts/plgx/KeeDroidSign.plgx`, `if-no-files-found: error`, `retention-days: 7`
- [X] T006 [US1] Add job `publish` to `.github/workflows/release.yml` (`ubuntu-latest`, `needs: build`, `permissions: contents: write`, `env GH_TOKEN: ${{ github.token }}`, `GH_REPO: ${{ github.repository }}`): download artifact `release-plgx`; if `gh release view <tag>` succeeds → when `inputs.replace_asset == true` run `gh release upload <tag> KeeDroidSign.plgx --clobber`, else fail with `::error::Release <tag> already exists`; otherwise `gh release create <tag> KeeDroidSign.plgx --verify-tag --title <version> --generate-notes` plus `--prerelease --latest=false` when `prerelease == true`; finally append the release URL to `$GITHUB_STEP_SUMMARY`; pass tag/version via `env:` (never inline `${{ }}` in scripts)
- [X] T007 [US1] Lint `.github/workflows/release.yml` with actionlint (scratchpad binary) and fix all findings

**Checkpoint**: Release workflow ready for merge

---

## Phase 4: User Story 2 - Publish version 1.0.0 (Priority: P1)

**Goal**: Release `1.0.0` with `KeeDroidSign.plgx` is public

**Independent Test**: `gh release view v1.0.0` shows title 1.0.0, latest, exactly one asset `KeeDroidSign.plgx`; the downloaded PLGX loads in KeePass as 1.0.0.0 (quickstart §3–4)

**Depends on**: US1 merged into `main`

### Implementation for User Story 2

- [X] T008 [P] [US2] Set `<Version>1.0.0</Version>` in `Directory.Build.props` and `AssemblyVersion`/`AssemblyFileVersion` `1.0.0.0` in `src/KeeDroidSign/Properties/AssemblyInfo.cs`
- [X] T009 [P] [US2] Add test `PluginVersion_MatchesCoreVersion` to `tests/KeeDroidSign.Tests/UI/PluginSmokeTests.cs`: `typeof(KeeDroidSignExt).Assembly.GetName().Version` equals `typeof(KeeDroidSign.Core.Keystore.KeystoreGenerator).Assembly.GetName().Version`
- [X] T010 [US2] Run `dotnet build`/`dotnet test` and `Build-Plgx.ps1 -Version 1.0.0` locally; all green
- [X] T011 [US2] After the PR is merged and the maintainer confirms: create signed annotated tag `git tag -s v1.0.0 -m "KeeDroidSign 1.0.0"` on the updated `main`, push it, watch the **Release** run, and verify with `gh release view v1.0.0 --json name,isLatest,isPrerelease,assets` (one asset `KeeDroidSign.plgx`)

**Checkpoint**: 1.0.0 published

---

## Phase 5: Polish & Cross-Cutting Concerns

- [X] T012 [P] Update `README.md`: installation step 1 downloads `KeeDroidSign.plgx` from the latest release (link `https://github.com/kolod/kee-droid-sign/releases/latest`); new section "Releasing" (push `vX.Y.Z` tag on `main`; pre-release suffix; manual re-run with `replace_asset`)
- [X] T013 Constitution review: English only, `keepass/` unchanged, no secrets, least privilege in `release.yml`; mark tasks done in this file

---

## Dependencies & Execution Order

```text
Foundational (T001–T002) → US1 (T003–T007) ─┐
US2 code (T008–T010) ───────────────────────┼─> PR merged → T011 (tag v1.0.0, release)
Polish (T012–T013) ─────────────────────────┘
```

- T003–T006 edit the same file: sequential.
- T008, T009, T012 are independent of the workflow and can run in parallel with US1.
- T011 is the only outward-facing step and requires the merged workflow and the maintainer's confirmation.

## Parallel Example

```text
Task: "Set version 1.0.0 in Directory.Build.props and AssemblyInfo.cs"
Task: "Add PluginVersion_MatchesCoreVersion test"
Task: "Update README installation and Releasing section"
```

## Implementation Strategy

1. Foundational + US1 + US2 code + Polish in one PR (`004-plgx-release` → `main`).
2. Merge after CI is green.
3. Tag `v1.0.0` (T011) — the release workflow publishes the PLGX.
