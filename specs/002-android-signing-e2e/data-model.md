# Data Model: Android Signing End-to-End Test

**Feature**: [spec.md](./spec.md) | **Date**: 2026-10-08

## Sample App (`android/app`)

| Attribute | Value |
|-----------|-------|
| Application ID | `io.github.kolod.keedroidsign.sample` |
| Version | `versionCode` from `-PappVersionCode` (default 1), `versionName` from `-PappVersionName` (default `1.0`) |
| Screens | 1 (`MainActivity`, text "Hello, World!") |
| Permissions | none |

## keystore.properties (CI-generated, git-ignored)

| Key | Source secret |
|-----|---------------|
| `storeFile` | constant `release.keystore.jks` (decoded from `ANDROID_KEYSTORE_BASE64`) |
| `storePassword` | `ANDROID_KEYSTORE_PASSWORD` |
| `keyAlias` | `ANDROID_KEY_ALIAS` |
| `keyPassword` | `ANDROID_KEY_PASSWORD` |

## Signing Report (`signing-report.json`, workflow artifact)

| Field | Type | Example |
|-------|------|---------|
| `request_id` | string | `"7f3c…"` (workflow input, empty for push runs) |
| `commit` | string | `github.sha` |
| `apk` | string | `"keedroidsign-sample.apk"` |
| `signer_count` | int | `1` |
| `sha256` | string | `"AA:BB:…"` (32 pairs, upper-case, same format as feature 001) |

## Workflow Run (as seen by the live test)

| Field | Source | Notes |
|-------|--------|-------|
| `RunId` | dispatch response `workflow_run_id` | primary match key |
| `HtmlUrl` | dispatch response `html_url` | printed on failure |
| `DisplayTitle` | `GET …/runs/{id}` `display_title` | must contain `RequestId` |
| `Status` | `queued` → `in_progress` → `completed` | polled every 15 s |
| `Conclusion` | `success` \| `failure` \| `cancelled` \| … | test requires `success` |

## Live Test Run

| Field | Notes |
|-------|-------|
| `RequestId` | new GUID per run |
| `Keystore` | `SigningKeystore` from `KeystoreGenerator` (RSA 2048 for speed; spec does not require 4096 here) |
| `ExpectedFingerprint` | `CertificateFingerprint.Compute(Keystore.CertificateDer)` |

### State transitions

```text
Generate key → Export secrets (overwrite=true) → Dispatch workflow → Poll run
   → completed/success → Download signing-report → compare sha256 → PASS / FAIL
   → completed/other or timeout → FAIL (message includes HtmlUrl)
```
