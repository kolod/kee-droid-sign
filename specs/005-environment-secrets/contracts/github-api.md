# Contract: GitHub API Usage (Core)

All requests: `https://api.github.com`, `X-GitHub-Api-Version: 2022-11-28`, bearer token. Path
segments (owner, repo, environment, secret name) are escaped with `Uri.EscapeDataString`. Error
messages are fixed English texts; never the token, a secret value or a response body.

## GitHubClient additions

| Method | Request | Result mapping |
|--------|---------|----------------|
| `ListSecretNamesAsync(SecretScope)` | GET `{base}?per_page=100&page=n` | as today |
| `GetPublicKeyAsync(SecretScope)` | GET `{base}/public-key` | as today; 404 in environment scope → `EnvironmentNotFoundException` |
| `PutSecretAsync(SecretScope, name, value, keyId)` | PUT `{base}/{name}` | 201 Created, 204 Updated, 403/404 "Insufficient permissions to write environment secrets." (scope-specific text) |
| `DeleteSecretAsync(RepositoryTarget, name)` | DELETE `repos/{o}/{r}/actions/secrets/{name}` | 204 or 404 → Deleted; 403 → Failed "Insufficient permissions to delete repository secrets." |
| `GetEnvironmentAsync(repo, env)` | GET `repos/{o}/{r}/environments/{env}` | 200 → policy + rules; 404 → NotFound; 403 → NoPermission |
| `PutEnvironmentAsync(repo, env, body)` | PUT `repos/{o}/{r}/environments/{env}` | 200 ok; 403/404/422 → `NeedsAdministration` / Failed |
| `ListBranchPoliciesAsync(repo, env)` | GET `.../deployment-branch-policies?per_page=100` | names + types |
| `AddBranchPolicyAsync(repo, env, name, type)` | POST `.../deployment-branch-policies` | 200 or 303 → ok; 403/404 → `NeedsAdministration` |

`{base}` = `repos/{o}/{r}/actions/secrets` (repository) or
`repos/{o}/{r}/environments/{env}/secrets` (environment). Existing `RepositoryTarget` overloads stay
and use the repository scope.

## SecretExporter

- `PlanAsync(SecretScope, mapping, ct)`:
  - environment scope: public key check (R2) → environment status (R3) → list environment secrets;
  - repository scope: unchanged.
- `ExportAsync(SecretScope, keystore, mapping, overwrite, ct)`: unchanged flow against the scope's
  public key and endpoints.
- `FindRepositoryCopiesAsync(repo, mapping, ct)` → `RepositoryCopies`.
- `DeleteRepositorySecretsAsync(repo, names, ct)` → `ExportResult` (Deleted/Failed per name).

## EnvironmentProtector

`ProtectAsync(repo, env, ct)` → `ProtectionResult`, algorithm in research R4. PUT body when creating or
switching from "all branches":

```json
{
  "wait_timer": <existing or omitted>,
  "prevent_self_review": <existing or omitted>,
  "reviewers": [ {"type": "User|Team", "id": <id>} ... ] | omitted,
  "deployment_branch_policy": { "protected_branches": false, "custom_branch_policies": true }
}
```

Policies added: `{ "name": "main", "type": "branch" }`, `{ "name": "v*", "type": "tag" }`.

## Fake handler (tests)

`FakeGitHubHandler` gains environments: a dictionary `env → {exists, policy, rules, secrets,
branchPolicies}`, per-endpoint forced statuses (403/404) and a request log, so tests assert that no
repository-level PUT happens in environment scope and no DELETE happens before all writes succeeded.

## Repository inspection (added after review, research R11)

| Method | Request | Result |
|--------|---------|--------|
| `GetDefaultBranchAsync(repo)` | GET `repos/{o}/{r}` → `default_branch` | name; errors → `GitHubApiException` |
| `GetBranchRuleTypesAsync(repo, branch)` | GET `repos/{o}/{r}/rules/branches/{branch}` | rule `type`s, or null when not readable |
| `IsBranchProtectedClassicAsync(repo, branch)` | GET `repos/{o}/{r}/branches/{branch}` → `protected` | bool, or null on 403/404 |
| `ListTagRulesetsAsync(repo)` | GET `repos/{o}/{r}/rulesets?targets=tag&includes_parents=true&per_page=100`, then GET `.../rulesets/{id}` | `enforcement`, include/exclude, rule types; null when not readable |

`RepositoryInspector.InspectAsync(SecretScope, SecretMapping, ct)` → `RepositoryReport`:
default branch + `RefProtection` (Protected / Unprotected / Unknown), tag creation `RefProtection`,
environment (existence, policy kind, existing patterns), proposed patterns, secrets to create /
overwrite (empty when the environment is missing), repository copies.

`EnvironmentProtector.ProtectAsync(repo, env, patterns, ct)` adds exactly the given
`(name, type)` patterns (no fixed list).
