# Research: Export Signing Secrets to a GitHub Environment

Sources: GitHub REST docs (API version 2022-11-28) — "Actions secrets", "Deployment environments",
"Deployment branch policies", "Permissions required for fine-grained personal access tokens"
(checked 2026-10-08).

## R1. Endpoints and fine-grained permissions

| Purpose | Endpoint | Fine-grained permission |
|---------|----------|-------------------------|
| Environment public key | `GET /repos/{o}/{r}/environments/{env}/secrets/public-key` | Environments: read |
| List environment secrets | `GET /repos/{o}/{r}/environments/{env}/secrets` | Environments: read |
| Write environment secret | `PUT /repos/{o}/{r}/environments/{env}/secrets/{name}` | Environments: write |
| Get environment (policy) | `GET /repos/{o}/{r}/environments/{env}` | Actions: read |
| List branch policies | `GET /repos/{o}/{r}/environments/{env}/deployment-branch-policies` | Actions: read |
| Create / update environment | `PUT /repos/{o}/{r}/environments/{env}` | Administration: write |
| Create branch policy | `POST /repos/{o}/{r}/environments/{env}/deployment-branch-policies` | Administration: write |
| Delete repository secret | `DELETE /repos/{o}/{r}/actions/secrets/{name}` | Secrets: write |

Classic tokens: `repo` covers all of them (Administration endpoints additionally need the token
owner to be a repository admin).

- **Decision**: keep API version `2022-11-28` and the `/repos/{owner}/{repo}/environments/...`
  paths (no `repository_id` variants).
- **Rationale**: same client, same headers; the owner/repo paths are current.
- **Alternatives**: `/repositories/{id}/environments/...` (legacy, needs an extra lookup) — rejected.

## R2. Detecting a missing environment (FR-003) with the export token

- **Decision**: the export check reads the environment public key
  (`.../environments/{env}/secrets/public-key`). 404 → environment not found; 403 → missing
  *Environments* permission. Only afterwards `GET /environments/{env}` reads the branch policy; a
  403/404 there means "protection could not be checked (needs Actions: read)" and is shown as a
  warning, not an error.
- **Rationale**: the public key is needed anyway, and its permission (Environments) is the one the
  export must have. `GET /environments/{env}` needs *Actions: read*, which an export-only token may
  lack; it must not block the export.
- **Risk**: GitHub may answer 404 for both "no environment" and "no access". Mitigation: the
  repository access check (existing `CheckRepositoryAccessAsync` first step) runs before; the
  message for 404 names both possibilities. Verified live in quickstart step Q3.

## R3. Reading the branch policy (FR-006)

`deployment_branch_policy` in the environment response:

| Value | Meaning | Plugin |
|-------|---------|--------|
| `null` | all branches and tags may deploy | warning "unrestricted" |
| `{protected_branches: true, custom_branch_policies: false}` | protected branches only | no warning |
| `{protected_branches: false, custom_branch_policies: true}` | custom patterns | no warning |

- **Decision**: warn only for `null`. Custom patterns are not inspected for content (a user may
  deliberately use other names than `main`/`v*`).

## R4. "Create / protect environment" (FR-006a)

Algorithm:
1. `GET /environments/{env}` — 404 → create; 200 → inspect; 403 → "needs Actions: read" + manual
   steps.
2. If missing, or policy is `null`: `PUT /environments/{env}` with
   `deployment_branch_policy = {protected_branches: false, custom_branch_policies: true}` and the
   **existing** `wait_timer`, `reviewers` and `prevent_self_review` copied from the GET response
   (`protection_rules` items `wait_timer` / `required_reviewers`).
3. If policy is `protected_branches: true`: change nothing, report "already restricted to protected
   branches".
4. With custom policies: list policies; `POST` the missing ones of `{name: "main", type: "branch"}`
   and `{name: "v*", type: "tag"}`. 303 (already exists) counts as success.
5. 403/404 on PUT or POST → result "needs Administration: write" + manual steps; nothing else is
   attempted.

- **Rationale**: GitHub's docs do not state whether PUT clears omitted protection rules; echoing the
  existing ones keeps reviewers and wait timer safe either way (spec US3 scenario 3).
- **Alternatives**: PUT only the branch policy — rejected (may drop reviewers); refusing to touch an
  existing environment — rejected (the common case is an environment created with defaults).

## R5. Environment name rules (FR-001)

GitHub allows slashes in environment names (they must be sent as `%2F`). .NET Framework's `Uri`
handling of `%2F` in paths differs between runtime compatibility modes, and KeePass decides that
mode, not the plugin.

- **Decision**: the plugin accepts names of 1–255 characters without `/`, control characters, or
  leading/trailing spaces; other characters are escaped with `Uri.EscapeDataString`.
- **Alternatives**: allow `/` — rejected (escaping cannot be guaranteed under KeePass's runtime
  settings).

## R6. Per-app override storage (FR-001a)

- **Decision**: string field `DroidSign.ExportTarget` on the app's keystore entry:
  absent → use the default; `repository` → repository level; `environment:<name>` → that
  environment. Unknown values → treated as absent, with a warning on the tab.
- **Rationale**: explicit prefix keeps "repository level" distinguishable from any environment name;
  the field follows the existing `DroidSign.Role` convention and survives KeePass sync/export.
- **Alternatives**: empty value meaning repository level — rejected (KeePass users can easily create
  or clear values by accident); separate fields — rejected (two fields can contradict each other).

## R7. Default for new installations vs upgrades (FR-001)

- **Decision**: when `KeeDroidSign.GitHubEnvironment` is absent from KeePass's config, the default
  is `release` if no other `KeeDroidSign.*` setting exists (new installation) and empty if
  `KeeDroidSign.RootGroup` exists (settings were saved by 1.0.x). The first save writes the value,
  so the decision is made once.
- **Rationale**: 1.0.x writes all settings on the first OK in Options, and exporting requires a
  token entry chosen there, so every 1.0.x user who ever exported has `RootGroup` saved.

## R8. Repository-level copies (FR-005)

- **Decision**: after a successful environment export, list repository secrets; intersect with the
  four configured names; if any, ask (Yes/No) and delete with
  `DELETE /actions/secrets/{name}` (204 = deleted, 404 = already gone → counts as deleted).
  If listing fails with 403 (token without *Secrets* permission), show a warning "could not check
  repository-level secrets" instead of asking.
- **Rationale**: a token prepared only for environments may lack the Secrets permission; that must
  not turn a successful export into an error.

## R9. Sample workflow and live test

- **Decision**: the `sign` job gets `environment: release`; `build-debug` (pull requests) stays
  without environment. The live test reads `KDS_LIVE_ENVIRONMENT` (default `release`; the value
  `-` means repository level) and exports into that scope.
- **Repository setup order** (maintainer actions, each confirmed): create/protect `release`
  (plugin action or UI) → export a key into it → merge the workflow change (the push to `main`
  signs with environment secrets) → run the live test → delete the repository-level secrets →
  dispatch the workflow from a non-`main` branch and confirm GitHub rejects the deployment.

## R10. Version

- **Decision**: 1.1.0 (new feature, backward compatible). Bumped in this feature; the release itself
  is a separate, confirmed step.

## R11. Repository inspection (FR-006) — added after review

Users' repositories differ: the default branch may be `master`, and nothing guarantees it or `v*`
tags are protected. A deployment policy on an unprotected ref gives no protection.

| Check | Endpoint | Permission |
|-------|----------|------------|
| Default branch | `GET /repos/{o}/{r}` → `default_branch` | Metadata: read |
| Active ruleset rules on the branch | `GET /repos/{o}/{r}/rules/branches/{branch}` → `[{type}]` | Metadata: read |
| Classic branch protection | `GET /repos/{o}/{r}/branches/{branch}` → `protected` | Contents: read (optional) |
| Tag rulesets | `GET /repos/{o}/{r}/rulesets?targets=tag&includes_parents=true`, then `GET .../rulesets/{id}` → `enforcement`, `conditions.ref_name.include/exclude`, `rules[].type` | Metadata: read |
| Environment policies | `GET .../environments/{env}` and `.../deployment-branch-policies` | Actions: read |

- **Decision**: branch *protected* = active rules contain `pull_request` or `update`, or classic
  `protected == true`. Unknown when the rules call fails and the classic call is forbidden.
  Tag creation *restricted* = an `active` tag ruleset whose include patterns match `refs/tags/v1.0.0`
  (fnmatch-style `*`, plus `~ALL`) and not its exclude patterns, containing a `creation` rule.
  Unknown when listing rulesets fails.
- **Decision**: proposed patterns = `{default branch} (branch)` and `v* (tag)`, minus those the
  environment already has; never a hard-coded `main`.
- **Rationale**: rulesets and rules endpoints need only Metadata, which every token has; classic
  protection is the only extra read and is optional.

## R12. Export wizard (FR-004) — added after review

- **Decision**: one modal `ExportWizardForm` with three pages (Target → Review → Results), opened by
  **Export to GitHub** on the DroidSign tab. The target page stores a changed per-app choice on the
  keystore entry. The review page runs the inspection asynchronously and shows findings (✓/⚠/✗/?)
  and actions as checkboxes; **Run** executes them in order: create/restrict environment → export →
  delete repository-level copies, stopping before the export if the environment is still missing,
  and before the cleanup if the export did not fully succeed.
- **Rationale**: all decisions in one place, with the facts visible before anything changes;
  clicking Run is the explicit confirmation required by the earlier message boxes.
- **Alternatives**: keep message boxes and tab buttons — rejected by the user (too scattered).

## R13. Fixing the warnings (FR-006b) — added after review

| Choice | Request | Permission |
|--------|---------|------------|
| Restrict `v*` tags | POST `repos/{o}/{r}/rulesets` — `target: tag`, include `refs/tags/v*`, rules `creation`, `update`, `deletion`, bypass `RepositoryRole` admin (`always`) | Administration: write |
| Drop `v*` from the environment | DELETE `.../environments/{env}/deployment-branch-policies/{id}` | Administration: write |
| Protect the default branch | POST `repos/{o}/{r}/rulesets` — `target: branch`, include `~DEFAULT_BRANCH`, rules `deletion`, `non_fast_forward`, `pull_request` (0 approvals), bypass admin (`pull_request` mode) | Administration: write |

- **Decision**: only add new rulesets named "KeeDroidSign: …" (never edit the user's own rulesets);
  "Leave as is" is the default; failures are reported and the export still runs.
- **Risk**: the docs do not list repository role IDs; `actor_id` 5 is the commonly used ID of the
  *admin* role. Verified live on `kolod/kee-droid-sign` during the maintainer steps (T044).
