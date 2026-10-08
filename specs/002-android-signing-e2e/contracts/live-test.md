# Contract: live end-to-end test

Project: `tests/KeeDroidSign.LiveTests` (not part of the default offline suite).

## Configuration (environment variables)

| Variable | Required | Default | Meaning |
|----------|----------|---------|---------|
| `KDS_LIVE_GITHUB_TOKEN` | yes (else skipped) | — | PAT: Actions RW, Secrets RW, Metadata R (classic: `repo`, `workflow`) |
| `KDS_LIVE_REPOSITORY` | no | `kolod/kee-droid-sign` | target repository |
| `KDS_LIVE_REF` | no | `main` | ref the workflow is dispatched on |
| `KDS_LIVE_TIMEOUT_MINUTES` | no | `20` | maximum wait for the run |

## GitHub API calls (test-only helper, beyond feature 001)

| Step | Request | Expected |
|------|---------|----------|
| Dispatch | `POST /repos/{o}/{r}/actions/workflows/android-sign.yml/dispatches` body `{"ref":REF,"inputs":{"request_id":ID}}`, header `X-GitHub-Api-Version: 2026-03-10` | 200 `{workflow_run_id, run_url, html_url}` |
| Poll | `GET /repos/{o}/{r}/actions/runs/{run_id}` | `status`, `conclusion`, `display_title` |
| Artifacts | `GET /repos/{o}/{r}/actions/runs/{run_id}/artifacts` | contains `signing-report` |
| Download | `GET /repos/{o}/{r}/actions/artifacts/{id}/zip` (no auto-redirect) | 302 `Location` → zip fetched without token |

Secrets are written with feature-001 `SecretExporter.ExportAsync(..., overwrite: true)`.

## Pass condition

`run.conclusion == "success"` **and** `display_title` contains the request ID **and**
`signing-report.sha256 == CertificateFingerprint.Compute(generated.CertificateDer)` **and**
`signer_count == 1`.

## Output rules

Allowed in output: run URL, request ID, expected/actual fingerprints, status. Never: token,
passwords, keystore bytes or Base64.
