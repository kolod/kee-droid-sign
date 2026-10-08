# Research: PLGX Release Publishing

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-10-08

## R1. Where the release version comes from

- **Decision**: The tag `vX.Y.Z[-pre]` is the single source. The workflow parses it into the numeric
  version `X.Y.Z` and passes it to the build:
  - `dotnet build -p:Version=X.Y.Z` for the SDK projects (core DLL packed into the PLGX);
  - `Build-Plgx.ps1 -Version X.Y.Z`, which rewrites `AssemblyVersion`/`AssemblyFileVersion` to
    `X.Y.Z.0` **in the staged copy** of `Properties/AssemblyInfo.cs` (the source file is not
    touched) and verifies the compiled plugin's file version equals `X.Y.Z.0`.
  The source versions (`Directory.Build.props` `<Version>`, plugin `AssemblyInfo.cs`) are raised to
  1.0.0 for this release; a mismatch with the tag only emits a `::warning::`.
- **Rationale**: KeePass compiles the PLGX from source, so the version must be in the staged
  sources; staging keeps the repository clean and makes any tag buildable.
- **Alternatives considered**: Committing a version bump from CI (needs write access to code);
  `-p:Version` alone (does not reach the PLGX sources because `GenerateAssemblyInfo` is off).

## R2. Release workflow shape

- **Decision**: `.github/workflows/release.yml`, two jobs:
  1. `build` on `windows-latest` (`permissions: contents: read`): checkout with full history and
     submodules, validate the tag, check the commit is in `origin/main`, build the KeePass
     reference, `dotnet build`/`dotnet test`, `Build-Plgx.ps1 -Version`, upload the PLGX as an
     artifact. `timeout-minutes: 30`.
  2. `publish` on `ubuntu-latest` (`needs: build`, `permissions: contents: write`): download the
     artifact and run `gh release create vX.Y.Z KeeDroidSign.plgx --verify-tag --title X.Y.Z
     --generate-notes` plus `--prerelease --latest=false` for pre-release tags.
- **Triggers**: `push` of tags `v*` (filtered further by a regex step) and `workflow_dispatch` with
  inputs `tag` (required) and `replace_asset` (boolean, default false).
- **Rationale**: Write permission exists only in the small publish job that never runs repository
  code (FR-008); a failure in `build` prevents publication entirely (SC-004).
- **Alternatives considered**: One job with `contents: write` (broader token during build);
  third-party release actions (`softprops/action-gh-release`) — `gh` is preinstalled and enough.

## R3. Tag validation and branch check

- **Decision**: Regex `^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$`;
  invalid tags end the run before building (FR-001, US1-5). Containment:
  `git merge-base --is-ancestor <tag commit> origin/main` (FR-007).

## R4. Idempotency

- **Decision**: In `publish`, if `gh release view vX.Y.Z` succeeds: with `replace_asset == true`
  (manual run only) → `gh release upload vX.Y.Z KeeDroidSign.plgx --clobber`; otherwise fail with
  "release already exists" (US1-6). No release is ever created twice.

## R5. Release 1.0.0

- **Decision**: Raise the source version to 1.0.0 in this feature's PR; after merging, create the
  annotated, signed tag `v1.0.0` on the merge commit of `main` and push it. The release workflow does
  the rest. This is an outward-facing action, confirmed with the maintainer before tagging.

## R6. Consistency test

- **Decision**: A plugin test asserts that the plugin assembly version (from `AssemblyInfo.cs`)
  equals the core assembly version (from `Directory.Build.props`), so local builds and releases do
  not drift.
