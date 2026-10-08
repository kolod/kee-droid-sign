# Implementation Plan: Export Signing Secrets to a GitHub Environment

**Branch**: `005-environment-secrets` | **Date**: 2026-10-08 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/005-environment-secrets/spec.md`

## Summary

Export the four signing secrets into a GitHub deployment environment instead of the repository.
Core gets a `SecretScope` (repository or environment) used by the existing client and exporter, an
environment status check (exists, branch policy), deletion of repository-level copies, and an
`EnvironmentProtector` that creates/restricts an environment to branch `main` and tags `v*` when the
token has Administration permission. The plugin gets a global default environment (`release` on new
installations, empty on upgrades), a per-app override stored on the keystore entry, an "Export to"
row with **Change…** and **Protect environment…** on the DroidSign tab, extended confirmations, and a
post-export cleanup question. The sample workflow's `sign` job declares `environment: release`; the
live test exports into the environment; README explains setup and permissions. Version 1.1.0.
See [research.md](./research.md).

## Technical Context

**Language/Version**: C# 12 / .NET Framework 4.8 (`KeeDroidSign.Core`, tests); C# 5 (`KeeDroidSign`
plugin sources, PLGX); GitHub Actions YAML

**Primary Dependencies**: KeePass 2.61.1 plugin API (submodule, unchanged); BouncyCastle.Cryptography
2.7 (sealed box, unchanged); `System.Net.Http`, `DataContractJsonSerializer`

**Storage**: KeePass config (`KeeDroidSign.GitHubEnvironment`); keystore entry string field
`DroidSign.ExportTarget`

**Testing**: xUnit (net48) with `FakeGitHubHandler`; `Build-Plgx.ps1` C# 5 check; opt-in live test
against `kolod/kee-droid-sign`

**Target Platform**: Windows, KeePass 2.61+; GitHub-hosted runners for the sample workflow

**Project Type**: desktop plugin + core library + CI workflow

**Performance Goals**: export with environment ≤ 3 extra API calls (public key, environment, list
repository secrets); live test < 20 min (SC-004)

**Constraints**: export token needs no Administration; no secret in errors/logs; repository-level
behaviour identical when no environment (SC-005); C# 5 in plugin sources

**Scale/Scope**: ~8 new/changed core types, ~6 plugin files, 1 new form, ~25 strings, 1 workflow,
live test, README

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / rule | Gate | Pre | Post |
|------------------|------|-----|------|
| I. English-only | code, docs, strings (resx English) | ✅ | ✅ |
| II. Plugin architecture | `keepass/` untouched; settings in Options tab; UI via injected tabs | ✅ | ✅ new form opened from the DroidSign tab only |
| III. Secrets | secrets only in KeePass and GitHub; least privilege | ✅ | ✅ Administration optional and only for the explicit action; fixed error texts; settings hold names only |
| IV. Scope | export of signing secrets to GitHub | ✅ | ✅ environment is a narrower export target |
| V. Tooling | in-process, no external tools | ✅ | ✅ |
| VI. Testable core | GitHub logic in Core with unit tests | ✅ | ✅ scope/status/protector/cleanup tested with the fake handler |
| Tech: single PLGX, C# 5 plugin sources verified | `Build-Plgx.ps1` | ✅ | ✅ |
| Workflow gate: build + tests before merge | `build` required check | ✅ | ✅ |

Result: **PASS** — no violations.

## Project Structure

### Documentation (this feature)

```text
specs/005-environment-secrets/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── github-api.md
│   └── plugin-ui.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/KeeDroidSign.Core/GitHub/
├── SecretScope.cs                 # new: repository or environment endpoint base
├── EnvironmentModels.cs           # new: EnvironmentStatus, ProtectionResult, DTOs, EnvironmentNotFoundException
├── EnvironmentNames.cs            # new: validation (R5)
├── EnvironmentProtector.cs        # new: create/restrict environment (R4)
├── GitHubClient.cs                # scope overloads, delete secret, environment + branch policy calls
├── GitHubClient.Environments.cs   # new partial: environment endpoints
├── SecretExporter.cs              # scope-aware plan/export, repository copies, cleanup
└── GitHubModels.cs                # ExportPlan/SecretOutcomeStatus additions

src/KeeDroidSign/
├── Settings/PluginSettings.cs     # DefaultEnvironment, new-vs-upgrade default
├── Storage/EntryFields.cs         # DroidSign.ExportTarget
├── Storage/Models.cs              # ExportTargetOverride, ExportTarget
├── Storage/DroidSignStore.Write.cs# SetExportTarget
├── Services/ExportService.cs      # scope resolution, cleanup, protect
├── Services/ExportConfirmation.cs # target text, warnings, cleanup question
├── UI/EntryTabControl.cs          # Export-to row, Change…, Protect environment…, cleanup flow
├── UI/ExportTargetForm.cs         # new
├── UI/OptionsTabControl.cs        # GitHub environment field
├── KeeDroidSignExt.cs             # pass "database changed" callback to the tab
└── Properties/Strings.resx (+ Strings.cs), AssemblyInfo.cs (1.1.0.0)

tests/KeeDroidSign.Core.Tests/GitHub/   # SecretScope, environment client, exporter scope, protector tests
tests/KeeDroidSign.Core.Tests/Support/FakeGitHubHandler.cs   # environments
tests/KeeDroidSign.Tests/{Settings,Storage,Services}/        # default, override, confirmation, export service
tests/KeeDroidSign.LiveTests/                                # KDS_LIVE_ENVIRONMENT

.github/workflows/android-sign.yml  # sign job: environment: release
Directory.Build.props               # 1.1.0
README.md                           # environment setup, permissions, user workflows
```

**Update after review (2026-10-08)**: the tab's target row, Change… and Protect environment… were
replaced by `UI/ExportWizardForm.cs` (Target → Review → Results) with `Services/ExportWizardText.cs`
and Core `GitHub/RepositoryInspector.cs` + `GitHubClient.Repository.cs` (research R11–R12);
`UI/ExportTargetForm.cs` and `Services/ExportConfirmation.cs` were removed.

**Structure Decision**: No new projects. GitHub logic stays in Core (modern C#); the plugin only
resolves the target, shows UI and stores the override (C# 5).

## Complexity Tracking

No constitution violations — section intentionally empty.
