# Contract: `release.yml` workflow and `Build-Plgx.ps1 -Version`

## Workflow `.github/workflows/release.yml` ("Release")

| Trigger | Inputs | Behaviour |
|---------|--------|-----------|
| `push` tags `v*` | — | validate → build → publish new release |
| `workflow_dispatch` | `tag` (string, required), `replace_asset` (bool, default `false`) | same for an existing tag; with `replace_asset` an existing release's asset is replaced |

Jobs:

| Job | Runner | Permissions | Steps |
|-----|--------|-------------|-------|
| `build` | `windows-latest`, 30 min timeout | `contents: read` | checkout (`fetch-depth: 0`, submodules, ref = tag) → validate tag (regex) → commit ∈ `origin/main` → warn if source version ≠ tag version → `Build-KeePassReference.ps1` → `dotnet build -c Release -p:Version=X.Y.Z` → `dotnet test --no-build` → `Build-Plgx.ps1 -Version X.Y.Z` → upload artifact `release-plgx` |
| `publish` | `ubuntu-latest` | `contents: write` | download artifact → if release exists: fail, or `gh release upload --clobber` when `replace_asset` → else `gh release create <tag> KeeDroidSign.plgx --verify-tag --title X.Y.Z --generate-notes [--prerelease --latest=false]` |

Outputs of `build`: `tag`, `version`, `prerelease` (`true`/`false`).

Failure modes:

| Situation | Result |
|-----------|--------|
| tag not matching regex | `build` fails at "Validate tag"; nothing published |
| commit not in `main` | `build` fails at "Check branch"; nothing published |
| test / compiler / product-name / version check fails | `build` fails; nothing published |
| release exists, push trigger or `replace_asset=false` | `publish` fails with "already exists"; release unchanged |

## `build/Build-Plgx.ps1` new parameter

`-Version X.Y.Z` (optional, `^\d+\.\d+\.\d+$`):

- rewrites `[assembly: AssemblyVersion("…")]` and `[assembly: AssemblyFileVersion("…")]` to
  `X.Y.Z.0` in the **staged** `Properties/AssemblyInfo.cs` (fails if either attribute is not found);
- after the verification compile, fails unless the compiled plugin's `FileVersion` is `X.Y.Z.0`.

Without `-Version` the source version is used unchanged (local builds).
