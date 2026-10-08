# Feature Specification: Export Signing Secrets to a GitHub Environment

**Feature Branch**: `005-environment-secrets`

**Created**: 2026-10-08

**Status**: Draft

**Input**: User description: "Export signing secrets to a GitHub environment (e.g. \"release\")
instead of repository-level secrets. New plugin setting \"GitHub environment\" (empty = repository
secrets as today). When set, export uses the environment's public key and environment secrets
endpoints; the environment must already exist (plugin explains how to create it with deployment
branch/tag policy main + v* tags). Update the sample signing workflow to use environment: release,
the live end-to-end test to export into the environment, and the README. Goal: secrets readable
only by jobs that declare the environment and run from main or release tags." (follow-up of the
2026-10-08 security review, finding #1)

## Scope Note

Today the plugin writes the four signing secrets at repository level. Any workflow on any branch of
the repository can read repository-level secrets, so anyone with write access (or a stolen token)
can push a branch with a workflow that sends the key elsewhere, without a pull request or review.
GitHub environments restrict their secrets to jobs that declare the environment and, with a
deployment branch/tag policy, only to runs from `main` or release tags. This feature lets the plugin
export into such an environment and moves this project's own signing workflow onto it.

## Clarifications

### Session 2026-10-08

- Q: Where is the environment name set — globally, per app, or both? → A: Both: a global default in
  the plugin settings, overridable per app (an environment name, or explicitly "repository level").
  The per-app choice is made in the export wizard and stored on the app's keystore entry.
- Q: May the plugin create/protect the environment itself? → A: Only as an explicit action the user
  selects; it needs a token with the repository Administration permission; without it nothing
  changes and the manual steps are shown. Plain export never needs that permission.
- Q: What is the default global environment, including after upgrading from 1.0.1? → A: `release`
  for new installations; installations that already have saved plugin settings keep repository
  level (empty) until the user changes it.
- Q: Which branch/tag patterns does protection add, given that users' repositories differ? → A: No
  hard-coded `main`: the plugin reads the repository's **default branch** and proposes it plus tag
  pattern `v*`; it checks whether the default branch is protected (rulesets or branch protection)
  and whether creating `v*` tags is restricted, warns about each unprotected ref, and the user
  chooses which patterns to add.
- Q: How are target choice, checks, protection, export and cleanup presented? → A: The DroidSign tab
  keeps only the **Export to GitHub** button. It opens an **export wizard**: (1) choose the target,
  (2) the plugin inspects the repository and proposes actions (create/restrict environment with
  selectable patterns, export, delete repository-level copies) with findings and warnings, (3) the
  user runs the selected actions and sees per-action results. Clicking **Run** is the confirmation;
  no separate message boxes.
- Q: The review warns about unprotected v* tags / default branch but offers nothing — what can the
  user do? → A: Each such warning gets its own choice on the review page ("Leave as is" by default):
  restrict v* tags with a tag ruleset (or drop v* from the environment), protect the default branch
  with a branch ruleset. (Supersedes the earlier "Export to" row, "Change…" and "Protect
  environment…" buttons on the tab and the separate Yes/No cleanup question.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Export into a configured environment (Priority: P1)

The developer sets **GitHub environment** to `release` in the plugin settings (or chooses a target
for one app in the wizard). When they export a key, the plugin writes the four secrets into that
environment instead of at repository level. The wizard's review page names both the repository and
the environment.

**Why this priority**: It is the security improvement itself.

**Independent Test**: With a simulated GitHub, export with the setting `release`: all requests go to
that environment's secret endpoints, encrypted with the environment's public key, and nothing is
written at repository level.

**Acceptance Scenarios**:

1. **Given** the setting is `release` and the environment exists, **When** the developer exports,
   **Then** the four secrets are created or updated in environment `release` and none at
   repository level.
2. **Given** the setting is empty, **When** the developer exports, **Then** the secrets are written
   at repository level exactly as in 1.0.1.
3. **Given** the target is `release`, **When** the review page is shown, **Then** it names the
   repository and the environment, e.g. "kolod/whiskergrid, environment release", and lists the
   secrets to create and to overwrite.
4. **Given** the target environment does not exist, **When** the review page is shown, **Then** it
   says so, proposes creating it (US3), and the export cannot run unless creation is selected; the
   manual steps are shown.
5. **Given** the token lacks permission for environment secrets, **When** the wizard inspects or
   exports, **Then** the message names the missing permission and nothing is written.
6. **Given** a new installation, **When** the settings are opened, **Then** the environment is
   `release`; **Given** an upgrade from 1.0.1 with saved settings, **Then** it is empty.
7. **Given** the global setting is `release` and the developer chooses "Repository secrets" for an
   app in the wizard, **When** that app is exported (now and later), **Then** its secrets go to
   repository level.

---

### User Story 2 - Leave no readable copy at repository level (Priority: P2)

When exporting into an environment while repository-level secrets with the same names still exist
(for example from an earlier export), the review page lists them as still readable by every
workflow and proposes deleting them (selected by default). Deletion runs only after all four
environment secrets were written successfully.

**Why this priority**: Without it, switching to an environment can silently keep the old exposure.

**Independent Test**: With a simulated GitHub holding repository-level copies, run the wizard with
the cleanup action selected: the copies are deleted after the export; unselected, they remain and
the result shows a warning; if the export fails, nothing is deleted.

**Acceptance Scenarios**:

1. **Given** repository-level secrets with the configured names exist, **When** the review page is
   shown, **Then** it lists them as "still readable by every workflow" with a selected
   "Delete repository-level copies" action.
2. **Given** the action is selected and the environment export succeeds, **Then** exactly those
   repository-level secrets are deleted and the results list them as deleted.
3. **Given** the action is not selected, **Then** they remain and the results show a warning.
4. **Given** the environment export fails (fully or partly), **Then** no repository-level secret is
   deleted and the results say why.
5. **Given** the token may not list repository secrets, **Then** the review page says the copies
   could not be checked, and no cleanup action is offered.

---

### User Story 3 - Inspect and protect the environment (Priority: P2)

The wizard inspects the repository: its default branch and whether that branch is protected, whether
creating `v*` tags is restricted, whether the environment exists, and which branches/tags may use it.
It warns about each weakness, e.g. "the environment allows every branch", "branch master is not
protected: anyone with write access can push a workflow that reads the key", "anyone with write
access can create a v* tag". If the environment is missing, allows every branch, or lacks the
proposed patterns, it proposes **Create / restrict environment** with selectable patterns: the
default branch (by its real name) and tag `v*`. This action needs a token with the repository
Administration permission; without it nothing changes and the manual steps are shown.

**Why this priority**: A deployment policy on an unprotected branch or tag gives a false sense of
security; users' repositories differ (default branch name, protection), so nothing may be assumed.

**Independent Test**: With a simulated GitHub: default branch `master` unprotected and no tag
ruleset → both warnings and proposed patterns `master` (branch) and `v*` (tag); protected branch and
a tag-creation ruleset → no such warnings; running the action creates/restricts the environment with
only the selected patterns; without Administration → manual steps, no change.

**Acceptance Scenarios**:

1. **Given** the repository's default branch is `master`, **When** the review page is shown, **Then**
   the proposed branch pattern is `master`, never a hard-coded `main`.
2. **Given** the default branch has no active pull-request/update rule and no branch protection,
   **Then** the review page warns that it is unprotected; **Given** it is protected, **Then** no
   such warning appears; **Given** protection cannot be read, **Then** it says "could not be
   checked".
3. **Given** no active tag ruleset restricts creating `v*` tags, **Then** the review page warns
   that anyone with write access can create such a tag.
4. **Given** the environment allows every branch, **Then** the review page warns that it does not
   protect the secrets yet.
5. **Given** a token with Administration permission and the action selected with some patterns,
   **When** the developer runs the wizard, **Then** the environment exists afterwards with custom
   deployment policies including exactly the selected new patterns, and existing policies, reviewers
   and wait timer are kept.
6. **Given** a token without Administration permission, **When** the action runs, **Then** nothing
   changes, the export does not run if the environment is still missing, and the results show the
   missing permission and the manual steps.

---

### User Story 4 - This project's signing workflow and live test use the environment (Priority: P2)

The sample signing workflow reads the signing secrets from environment `release`; the live
end-to-end test exports into that environment; the repository's own environment `release` is
restricted to `main` and `v*` tags; the old repository-level signing secrets are removed.

**Why this priority**: Proves the feature end to end and removes this repository's own exposure.

**Independent Test**: After the change, the live end-to-end test passes; a workflow run from a
non-`main` branch cannot read the secrets.

**Acceptance Scenarios**:

1. **Given** environment `release` with the secrets, **When** the signing workflow runs on `main`,
   **Then** it signs the APK with the key from the environment.
2. **Given** a run from another branch, **When** the job declares environment `release`, **Then**
   GitHub refuses the deployment and no secret is available.
3. **Given** the live test runs with the environment configured, **When** it completes, **Then** the
   APK fingerprint equals the exported key's fingerprint.

### Edge Cases

- Environment names are case-insensitive on GitHub; the plugin uses the configured name as is.
- The environment exists but has required reviewers: export still works (reviewers apply to
  workflow runs, not to writing secrets).
- The repository-level deletion partially fails: results list each secret; nothing is retried
  silently.
- Private repositories on plans without environment support for private repos: GitHub rejects the
  environment or its policies; the plugin reports this plainly and suggests repository secrets.
- The environment uses "protected branches only": no patterns are proposed; the review page notes
  that only protected branches can use it.
- Classic branch protection cannot be read without the Contents permission: the branch is reported
  as "could not be checked" unless a ruleset already protects it.
- The wizard is closed while a step runs: the running request is cancelled; already written secrets
  stay as they are and nothing else runs.
- Organization-level secrets with the same names: out of scope (the plugin neither reads nor writes
  them).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The plugin settings MUST offer a default **GitHub environment** name (empty means
  repository-level secrets as in 1.0.1); the name is validated (no `/`, at most 255 characters).
  The default is `release` for a new installation; an installation that already has saved plugin
  settings (upgrade from 1.0.1) keeps empty until the user changes it.
- **FR-001a**: Each app MAY override the global setting with its own environment name or with an
  explicit "repository level" choice, made on the wizard's target page and stored on the app's
  keystore entry; without an override the global setting applies.
- **FR-002**: When an environment is the target, export MUST write the four secrets into that
  environment, encrypted with the environment's own public key, and MUST NOT write repository-level
  secrets.
- **FR-003**: Before writing, the plugin MUST verify that the environment exists; the export MUST NOT
  run while it is missing (unless the selected create action succeeded first), and the wizard MUST
  show how to create it manually.
- **FR-004**: The DroidSign tab MUST keep a single **Export to GitHub** button that opens the export
  wizard: target page → inspection and review page (repository, environment, findings, proposed
  actions as checkboxes) → results page. Running the selected actions is the confirmation.
- **FR-005**: When the target is an environment, the review page MUST list repository-level secrets
  with the configured names and propose deleting them (selected by default); deletion runs only
  after all four environment secrets were written successfully.
- **FR-006**: The inspection MUST report: environment existence and branch policy; the repository's
  default branch and whether it is protected (active ruleset pull-request/update rule or branch
  protection), or "could not be checked"; whether creating `v*` tags is restricted by an active tag
  ruleset. Every weakness MUST be shown as a warning.
- **FR-006a**: When the environment is missing, allows every branch, or lacks the proposed patterns,
  the wizard MUST offer **Create / restrict environment** with selectable patterns (default branch by
  its real name, tag `v*`). It creates the environment if missing and adds only the selected patterns,
  keeping existing policies and protection rules. Without the Administration permission it changes
  nothing and shows the manual steps; plain export never requires that permission. Plain export MUST
  never create or modify environments.
- **FR-006b**: For each warning that undermines the environment, the review page MUST offer a choice
  with "Leave as is" as the default: for `v*` tags — restrict creating/moving/deleting them to
  repository admins (new tag ruleset) or, if the environment already allows `v*`, remove that
  pattern; for an unprotected default branch — require pull requests and block force pushes and
  deletion (new branch ruleset; admins may merge without approvals). These need the Administration
  permission; a failure is reported and does not stop the export.
- **FR-007**: Errors MUST distinguish "environment not found" and "insufficient permission" and
  MUST NOT contain secret values.
- **FR-008**: The sample signing workflow MUST read the secrets through environment `release`.
- **FR-009**: The live end-to-end test MUST export into the environment configured for it (default
  `release`) and still verify the APK fingerprint.
- **FR-010**: The README MUST explain the wizard, how to create the environment (deployment branches
  and tags: the default branch and `v*`, optional required reviewers), why the default branch must be
  protected and `v*` tag creation restricted, the token permissions, and the new setting; and, for
  users' own workflows, that the signing job must declare `environment: <name>`, receives the
  secrets only when run from an allowed ref, and that pull-request runs should build without
  signing (as the sample workflow does).

### Key Entities

- **Plugin Settings**: + default GitHub environment name (empty = repository level; `release` on
  new installations, empty on upgrades).
- **App (keystore entry)**: + optional export target override (not set = use the default; an
  environment name; or explicitly repository level).
- **Export Target**: repository plus effective environment (app override, else default); determines
  public key, secret endpoints, and the wording of the review page.
- **Repository Report**: result of the inspection — default branch and its protection, tag creation
  restriction, environment status and policies, secrets to create/overwrite, repository-level copies.
- **Proposed Actions**: create/restrict environment (with selected patterns), export, delete
  repository-level copies; each with a per-action result.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With an environment configured, 100% of exported secrets land in that environment and
  0 at repository level.
- **SC-002**: After switching to an environment with the cleanup action selected, no repository-level
  copy of the four secrets remains.
- **SC-003**: A workflow run from a ref that the environment does not allow cannot obtain the signing
  secrets of this repository.
- **SC-004**: The live end-to-end test passes with the environment, in under 20 minutes.
- **SC-005**: With the setting empty, the secrets written are exactly those of version 1.0.1
  (existing exporter tests unchanged and passing).
- **SC-006**: No proposed branch pattern is ever a name the repository's default branch does not
  have; every unprotected allowed ref is reported before anything runs.

## Assumptions

- The global environment setting is the default for all apps; an app can override it (FR-001a).
  Existing apps have no override, so they follow the global setting.
- Inspection uses only read permissions every export token has (Metadata, Environments; Contents for
  classic branch protection is optional); creating/restricting needs Administration and is never
  required for export. The README recommends granting Administration only to those who want it.
- "Protected" means: an active ruleset with a pull-request or update-restriction rule applies to the
  branch, or classic branch protection is enabled. "Tag creation restricted" means: an active tag
  ruleset whose conditions match `refs/tags/v*` contains a creation rule.
- Setting up environment `release` on `kolod/kee-droid-sign` and deleting its old repository-level
  secrets are maintainer actions done once during implementation, after confirmation.
- A plugin release (e.g. 1.1.0) follows this feature through the existing release process.
