# Implementation Plan: PLGX Release Publishing

**Branch**: `004-plgx-release` | **Date**: 2026-10-08 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/004-plgx-release/spec.md`

## Summary

Add a **Release** GitHub Actions workflow: pushing a `vX.Y.Z[-pre]` tag on a commit in `main`
builds and verifies the plugin on Windows (tests, KeePass C# 5 compiler check, product name, and a
new version check), stamps the tag version into the PLGX's staged `AssemblyInfo.cs`, and publishes a
GitHub Release whose only asset is `KeeDroidSign.plgx`, with generated notes. Then raise the source
version to 1.0.0 and publish release 1.0.0. See [research.md](./research.md).

## Technical Context

**Language/Version**: GitHub Actions YAML; PowerShell (Windows PowerShell 5.1 for `Build-Plgx.ps1`,
pwsh/bash in workflow steps); C# only for one consistency test

**Primary Dependencies**: existing `build/Build-KeePassReference.ps1`, `build/Build-Plgx.ps1`;
`actions/checkout`, `actions/setup-dotnet`, `actions/upload-artifact`, `actions/download-artifact`;
preinstalled `gh` CLI

**Storage**: N/A (GitHub Releases)

**Testing**: existing test suites in the release build; actionlint; local `Build-Plgx.ps1 -Version`
runs; the 1.0.0 release itself as the end-to-end check

**Target Platform**: GitHub-hosted `windows-latest` (build) and `ubuntu-latest` (publish)

**Project Type**: CI/CD workflow + build-script change

**Performance Goals**: tag → downloadable release in < 10 min (SC-001; the current .NET CI takes
~1.5 min)

**Constraints**: least privilege (write only in `publish`), no secrets, exactly one asset, no
release on any failed check

**Scale/Scope**: 1 workflow (2 jobs), 1 script parameter, version bump, 1 test, README update

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / rule | Gate | Pre | Post |
|------------------|------|-----|------|
| I. English-only | workflow, scripts, docs in English | ✅ | ✅ |
| II. Plugin architecture | `keepass/` untouched | ✅ | ✅ (reference build only) |
| III. Secrets | no secrets added; least privilege | ✅ | ✅ `GITHUB_TOKEN` with `contents: write` only in `publish`, which runs no repository code |
| IV. Scope | distribution of the plugin | ✅ | ✅ |
| V. Tooling | no runtime tools | ✅ N/A | ✅ N/A |
| VI. Testable core | release build runs all tests | ✅ | ✅ |
| Tech: single `.plgx`, C# 5 verified on every build (v1.4.0) | release ships only the PLGX built by `Build-Plgx.ps1` | ✅ | ✅ |
| Workflow gate: build + tests before merge/release | | ✅ | ✅ `publish` needs `build` |

Result: **PASS** — no violations.

## Project Structure

### Documentation (this feature)

```text
specs/004-plgx-release/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/release-workflow.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
.github/workflows/release.yml                 # new: Release workflow (build + publish jobs)
build/Build-Plgx.ps1                          # + -Version parameter (staged AssemblyInfo, check)
Directory.Build.props                         # <Version>1.0.0</Version>
src/KeeDroidSign/Properties/AssemblyInfo.cs   # 1.0.0.0
tests/KeeDroidSign.Tests/UI/PluginSmokeTests.cs  # + plugin version == core version
README.md                                     # download from the latest release
```

**Structure Decision**: No new projects. The release workflow reuses the existing build scripts; the
only script change is the optional `-Version` parameter.

## Complexity Tracking

No constitution violations — section intentionally empty.
