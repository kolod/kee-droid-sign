# Contract: `android-sign.yml` workflow

File: `.github/workflows/android-sign.yml`

## Triggers

| Event | Condition | Jobs |
|-------|-----------|------|
| `workflow_dispatch` | input `request_id` (string, optional, default `""`) | `sign` |
| `push` | branch `main`, paths `android/**`, `.github/workflows/android-sign.yml` | `sign` |
| `pull_request` | same paths | `build-debug` only (no secrets) |

`run-name`: `Sign sample APK ${{ inputs.request_id || github.sha }}`

## Inputs consumed (repository secrets)

`ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`
— exactly the default mapping exported by `SecretExporter` (feature 001).

## Outputs

| Output | Name | Contents |
|--------|------|----------|
| Artifact | `sample-apk` | `keedroidsign-sample.apk` (signed release) |
| Artifact | `signing-report` | `signing-report.json` (see [data-model.md](../data-model.md)) |
| Job summary | — | table with request ID, commit, signer count, SHA-256 fingerprint |

## Failure behaviour

| Situation | Result |
|-----------|--------|
| Any secret missing | job fails at "Check signing secrets" with `::error::Missing secret NAME` per secret |
| Invalid Base64 / wrong password / wrong alias | Gradle `packageRelease` fails; no APK artifact |
| `keystore.properties` absent at build time | Gradle fails with "Release signing is not configured" (no debug-key fallback) |
| Signer count ≠ 1 | job fails at "Verify signature" |
| Any outcome | keystore and properties removed in an `if: always()` step |

## Permissions

`permissions: contents: read`
