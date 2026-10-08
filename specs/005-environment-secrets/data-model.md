# Data Model: Export Signing Secrets to a GitHub Environment

## Plugin settings (`PluginSettings`, KeePass config prefix `KeeDroidSign.`)

| Field | Config key | Type | Default | Validation |
|-------|-----------|------|---------|------------|
| DefaultEnvironment | `GitHubEnvironment` | string | `release` (new install) / empty (upgrade, see research R7) | empty, or 1–255 chars, no `/`, no control chars, no leading/trailing spaces |

Empty means repository level.

## App export target override (keystore entry field)

| Field | Values | Meaning |
|-------|--------|---------|
| `DroidSign.ExportTarget` | absent | use `DefaultEnvironment` |
| | `repository` | repository level |
| | `environment:<name>` | environment `<name>` (same validation as above) |
| | anything else | treated as absent; tab shows a warning |

Model: `ExportTargetOverride { Kind: Default | Repository | Environment; Environment: string }`,
read by `AppKeystore.TargetOverride`, written by `DroidSignStore.SetExportTarget(app, override)`
(touches the entry, marks the database modified).

## Export target (resolved)

`ExportTarget { Repository: RepositoryTarget; Environment: string (null = repository level);
FromApp: bool }` — `ExportTarget.Resolve(app, settings)`. `ToString()`:
`kolod/whiskergrid` or `kolod/whiskergrid, environment release`.

## Core: secret scope

`SecretScope { Repository: RepositoryTarget; Environment: string (null = repository) }` — decides the
endpoint base: `repos/{o}/{r}/actions/secrets` or `repos/{o}/{r}/environments/{env}/secrets`.

## Core: environment status

`EnvironmentStatus`:

| Field | Values |
|-------|--------|
| Exists | `Found`, `NotFound`, `NoPermission` (Environments) |
| BranchPolicy | `AllBranches`, `ProtectedBranches`, `CustomPolicies`, `Unknown` (no Actions: read) |

## Core: export plan (extended)

`ExportPlan` + `Scope: SecretScope`, `Environment: EnvironmentStatus` (null for repository scope).

Planning an environment export where `Exists != Found` throws `EnvironmentNotFoundException` /
`GitHubApiException` (no write is attempted).

## Core: export result (extended)

`SecretOutcomeStatus` + `Deleted`. Repository-level cleanup returns its own `ExportResult` with
`Deleted` / `Failed` outcomes.

`RepositoryCopies { Names: IReadOnlyList<string>; Checked: bool }` — `Checked = false` when the token
may not list repository secrets.

## Core: environment protection result

`ProtectionResult { Status: Protected | AlreadyRestricted | NeedsAdministration | NeedsActionsRead |
Failed; Created: bool; AddedPolicies: IReadOnlyList<string>; Message: string }`.

## State transitions (one export into an environment)

```text
Plan ── env NotFound/NoPermission ──► error message (+ "Create / protect environment" hint), stop
  │
  ▼ env Found (policy AllBranches → warning in confirmation; Unknown → note)
Confirm (Yes) ──► write 4 secrets ── any failed ──► results, stop (no cleanup question)
                                   │
                                   ▼ all succeeded
                        list repository-level copies ── none / not checked ──► done (+ note)
                                   │
                                   ▼ some
                        ask Yes/No ── Yes ──► delete, list outcomes
                                   └─ No ──► warning on the tab
```
