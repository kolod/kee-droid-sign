# Contract: Plugin UI and Workflow

All strings go to `Properties/Strings.resx` (English).

## Options → DroidSign tab

New row **GitHub environment** (text box) under the secret names, with the note:
"Secrets are exported into this environment of each repository, so only jobs that use the
environment can read them. Leave empty to export repository secrets (readable by every workflow).
Apps can choose their own target in the export wizard."
Invalid names block OK with the validation message (existing `OptionsNotSaved` flow).

## Key entry → DroidSign tab

Unchanged layout; the repository row keeps the single **Export to GitHub** button, which opens the
export wizard. No target row, no Change/Protect buttons on the tab.

## Export wizard (`ExportWizardForm`)

### Page 1 — Target

- Repository (link text, read-only) and app display name.
- Radio buttons: *Use the default ({environment X | repository secrets})*, *Repository secrets
  (readable by every workflow)*, *Environment:* [name].
- **Next**: a changed choice is stored on the keystore entry (`DroidSign.ExportTarget`), the main
  window is refreshed; then the inspection starts.

### Page 2 — Review

Header: `{owner/repo}, environment {env}` (or `{owner/repo}, repository secrets`).

Findings (one line each, prefix ✓ ok / ⚠ warning / ✗ blocking / ? unknown):

| Finding | Texts (essence) |
|---------|-----------------|
| Environment | exists / does not exist (✗ until created) / protected branches only / allows every branch (⚠) / allows: `main (branch)`, `v* (tag)` |
| Default branch | `master` is protected (✓) / not protected — anyone with write access can push a workflow that reads the key (⚠) / could not be checked (?) |
| Tag creation | creating `v*` tags is restricted (✓) / anyone with write access can create a `v*` tag (⚠) / could not be checked (?) — shown only when `v*` is or will be allowed |
| Secrets | N new, M overwritten (names) |
| Repository copies | names still readable by every workflow (⚠) / could not be checked (?) |

Actions (checkboxes, in run order):

1. **Create environment {env}** / **Restrict environment {env}** — shown when the environment is
   missing, allows every branch, or lacks proposed patterns; sub-checkboxes per proposed pattern
   (`{default branch} (branch)`, `v* (tag)`), all selected; note "needs a token with the
   Administration permission". Missing environment + unselected → export checkbox disabled.
2. **Export the 4 secrets** — always shown, selected.
3. **Delete repository-level copies** — shown when copies exist, selected.

Manual steps (shown when the environment is missing or not restricted):

```text
GitHub → repository → Settings → Environments → New environment "{env}"
→ Deployment branches and tags: Selected branches and tags
→ add "{default branch}" (branch) and "v*" (tag)
```

Buttons: **Back**, **Run**, **Cancel**.

### Page 3 — Results

One line per executed action and per secret (`ANDROID_KEY_ALIAS: Created`, `repository
ANDROID_KEY_ALIAS: Deleted`), skipped actions with the reason, final warnings (copies kept,
unprotected refs). Button **Close**. Closing during a run cancels it.

## Sample workflow (`.github/workflows/android-sign.yml`)

- job `sign`: `environment: release`; check-step error text mentions the environment;
- job `build-debug`: unchanged (no environment, no secrets).

## Live test environment variables

| Variable | Default | Meaning |
|----------|---------|---------|
| `KDS_LIVE_ENVIRONMENT` | `release` | target environment; `-` = repository level |
