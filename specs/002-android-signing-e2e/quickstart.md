# Quickstart: Validating the Android Signing End-to-End Test

**Feature**: [spec.md](./spec.md) | **Contracts**: [workflow.md](./contracts/workflow.md),
[live-test.md](./contracts/live-test.md)

## Prerequisites

- Android SDK with platform 37 and recent build-tools; `android/local.properties` containing
  `sdk.dir=...` (git-ignored) or `ANDROID_HOME` set.
- JDK 21 (Gradle daemon JVM).
- For the live test: a GitHub token as described in [live-test.md](./contracts/live-test.md), and
  this feature merged into `main` (workflow dispatch only works for workflows on the default branch).

## 1. Sample app builds without secrets (US1, SC-001)

```powershell
cd android
./gradlew :app:assembleDebug
```

Expected: `app/build/outputs/apk/debug/app-debug.apk` exists. Installing it
(`adb install -r ...`) and launching shows "Hello, World!".

## 2. Release build refuses to fall back to the debug key (FR-005)

```powershell
./gradlew :app:assembleRelease
```

Expected without `app/keystore.properties`: build **fails** with
"Release signing is not configured: keystore.properties not found".

## 3. Release build signs with a local keystore (US2 locally)

Generate a throwaway keystore with the plugin core (or `keytool`), place it as
`android/app/release.keystore.jks`, write `android/app/keystore.properties`, then:

```powershell
./gradlew :app:assembleRelease
& "$env:LOCALAPPDATA\Android\Sdk\build-tools\36.1.0\apksigner.bat" verify --print-certs app/build/outputs/apk/release/app-release.apk
```

Expected: exactly one signer whose SHA-256 equals the keystore's fingerprint. Delete both files
afterwards.

## 4. Workflow (US2, after merge)

Run **Actions → Sign sample APK → Run workflow**. Expected with valid secrets: success, artifacts
`sample-apk` and `signing-report`, fingerprint in the summary. With a secret deleted: failure at
"Check signing secrets" naming it.

## 5. Live test (US3)

```powershell
$env:KDS_LIVE_GITHUB_TOKEN = Read-Host -Prompt 'GitHub token' -MaskInput   # not saved in history
dotnet test tests/KeeDroidSign.LiveTests -c Release --logger "console;verbosity=detailed"
Remove-Item Env:KDS_LIVE_GITHUB_TOKEN
```

Expected: one test passes in under 20 minutes; output shows the run URL and matching fingerprints.
Without the variable: the test is reported as skipped. The default offline suite
(`dotnet test tests/KeeDroidSign.Core.Tests`) is unaffected.
