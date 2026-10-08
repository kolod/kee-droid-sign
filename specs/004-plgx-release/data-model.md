# Data Model: PLGX Release Publishing

**Feature**: [spec.md](./spec.md) | **Date**: 2026-10-08

## Version Tag

| Field | Example | Rule |
|-------|---------|------|
| Tag | `v1.0.0`, `v1.1.0-beta.1` | regex in research R3 |
| Numeric version | `1.0.0` | `X.Y.Z` part of the tag |
| Assembly version | `1.0.0.0` | numeric version + `.0` |
| Pre-release | `beta.1` or empty | non-empty → GitHub pre-release, not latest |
| Commit | tag target | must be an ancestor of `origin/main` |

## Release

| Field | Value |
|-------|-------|
| Title | numeric version (`1.0.0`) |
| Notes | GitHub-generated since the previous release |
| Flags | `prerelease` for pre-release tags; otherwise `latest` |
| Assets | exactly `KeeDroidSign.plgx` |

### State transitions

```text
tag pushed ──invalid──> no run output (stopped at validation)
     │valid
     ▼
build (tests, compiler check, product name, version check) ──fail──> no release
     │ok
     ▼
publish ──release exists & !replace──> fail (nothing changed)
     │                 └─exists & replace (manual)──> asset replaced
     ▼
release published
```

## Source version locations

| File | Field | 1.0.0 value |
|------|-------|-------------|
| `Directory.Build.props` | `<Version>` | `1.0.0` |
| `src/KeeDroidSign/Properties/AssemblyInfo.cs` | `AssemblyVersion`, `AssemblyFileVersion` | `1.0.0.0` |
