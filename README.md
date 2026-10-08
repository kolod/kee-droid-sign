# KeeDroidSign

A [KeePass 2.x](https://keepass.info/) plugin for managing Android app signing keys:

- generates an Android signing key and a JKS keystore and stores them in the KeePass database;
- exports the keystore and its credentials to a GitHub repository as GitHub Actions secrets;
- shows the SHA-256 certificate fingerprint (`B2:71:2B:...`) required by the Android Developer
  Console.

> **Status:** the core library (`KeeDroidSign.Core`) is implemented and tested. The KeePass plugin
> shell (Tools-menu window and Options tab) is a later feature.

## Repository layout

| Path | Contents |
|------|----------|
| `src/KeeDroidSign.Core/` | UI- and KeePass-independent core library (.NET Framework 4.8) |
| `tests/KeeDroidSign.Core.Tests/` | xUnit test suite (runs offline) |
| `tests/KeeDroidSign.LiveTests/` | Opt-in end-to-end test against real GitHub (skipped without a token) |
| `android/` | "Hello, World!" Kotlin app used only as a signing test fixture |
| `.github/workflows/android-sign.yml` | Builds and signs the sample APK from the repository secrets |
| `keepass/` | KeePass source code as a git submodule (read-only reference) |
| `specs/` | Feature specifications, plans and task lists |
| `.specify/memory/constitution.md` | Project principles |

## Build and test

Requirements: Windows, .NET Framework 4.8 runtime, .NET SDK 8.0 or newer.

```powershell
git submodule update --init
dotnet build KeeDroidSign.sln -c Release
dotnet test  KeeDroidSign.sln -c Release
```

No Java is needed. If a JDK is installed (found via `KDS_KEYTOOL`, `JAVA_HOME` or `PATH`), one extra
test checks that the JDK `keytool` opens the generated keystores; otherwise that test is skipped.

## Core capabilities

| Area | Entry point |
|------|-------------|
| Passwords | `Passwords.PasswordGenerator` — CSPRNG, never produces `$`, backtick, `\`, quotes or whitespace |
| Keystore | `Keystore.KeystoreGenerator` — RSA 4096 / SHA-256 / 30 years, JKS, fully in memory |
| Fingerprint | `Fingerprints.CertificateFingerprint` — SHA-256 or SHA-1, `AA:BB:...` format |
| GitHub | `GitHub.GitHubClient`, `GitHub.SecretExporter` — token check, repository access check, create/update secrets |

Secrets are encrypted with the repository public key (libsodium sealed box) before they leave the
machine. Existing secrets are overwritten only after explicit confirmation.

## GitHub Actions contract

The export writes these repository secrets (names are configurable):

| Secret | Value |
|--------|-------|
| `ANDROID_KEYSTORE_BASE64` | keystore file, single-line Base64 |
| `ANDROID_KEYSTORE_PASSWORD` | keystore password |
| `ANDROID_KEY_ALIAS` | key alias |
| `ANDROID_KEY_PASSWORD` | key password |

Example workflow steps that consume them:

```yaml
      - name: Decode signing keystore
        run: echo "${{ secrets.ANDROID_KEYSTORE_BASE64 }}" | base64 -d > app/release.keystore.jks

      - name: Write keystore.properties
        run: |
          cat > app/keystore.properties <<EOF
          storeFile=release.keystore.jks
          storePassword=${{ secrets.ANDROID_KEYSTORE_PASSWORD }}
          keyAlias=${{ secrets.ANDROID_KEY_ALIAS }}
          keyPassword=${{ secrets.ANDROID_KEY_PASSWORD }}
          EOF
```

Generated passwords are safe to use in this unquoted heredoc and in `.properties` files.

## GitHub token

Exporting secrets (and the end-to-end test) needs a GitHub personal access token. The token is used
only for HTTPS calls to `api.github.com` and is never written to logs or plugin settings.

### Create a token

**Fine-grained token (recommended)**

1. On GitHub open **Settings → Developer settings → Personal access tokens → Fine-grained tokens**
   (<https://github.com/settings/personal-access-tokens/new>) and click **Generate new token**.
2. **Token name**: e.g. `KeeDroidSign`; **Expiration**: as short as practical (e.g. 90 days).
3. **Resource owner**: the account or organization that owns the target repositories.
4. **Repository access**: *Only select repositories* → pick the app repositories (and
   `kee-droid-sign` itself if you run the end-to-end test).
5. **Repository permissions**:

   | Permission | Access | Needed for |
   |------------|--------|------------|
   | Secrets | Read and write | exporting the signing secrets |
   | Metadata | Read-only | selected automatically |
   | Actions | Read and write | end-to-end test only (start the workflow, download artifacts) |

6. Click **Generate token** and copy it immediately — GitHub shows it only once.

**Classic token (alternative)**: **Settings → Developer settings → Personal access tokens → Tokens
(classic) → Generate new token (classic)** with scope `repo` (plus `workflow` for the end-to-end
test). Classic tokens apply to all your repositories, so prefer fine-grained ones.

### Where to keep it

- **Plugin**: store the token in your KeePass database, e.g. an entry *GitHub – KeeDroidSign* with
  the token in the *Password* field. The plugin reads it from the database when exporting; it never
  stores the token anywhere else. (The plugin UI is a later feature; the core library already
  accepts the token as `GitHubCredential`.)
- **End-to-end test**: pass it through the `KDS_LIVE_GITHUB_TOKEN` environment variable for the
  current terminal session only, for example by copying it from KeePass:

  ```powershell
  $env:KDS_LIVE_GITHUB_TOKEN = Read-Host -Prompt 'GitHub token' -MaskInput
  dotnet test tests/KeeDroidSign.LiveTests -c Release
  Remove-Item Env:KDS_LIVE_GITHUB_TOKEN
  ```

  Do not use `setx` or the system environment-variable dialog: they store the token in plain text
  in the registry. Never put the token in a file inside the repository.

If a token leaks, revoke it at once on the same GitHub settings page.

## End-to-end signing test

The `android/` sample app and the **Sign sample APK** workflow verify the whole chain against this
repository:

1. the live test generates a throwaway key with `KeeDroidSign.Core`;
2. exports it to this repository's Actions secrets (the four secrets above);
3. dispatches the workflow, which builds the release APK, verifies its signature with `apksigner`
   and publishes `sample-apk` and `signing-report` artifacts;
4. passes only if the APK's signing certificate fingerprint equals the generated key's.

The release build never falls back to the debug key: without `keystore.properties` it fails.

Run it (only after the workflow is on `main`, a GitHub requirement for manual dispatch):

```powershell
$env:KDS_LIVE_GITHUB_TOKEN = Read-Host -Prompt 'GitHub token' -MaskInput
dotnet test tests/KeeDroidSign.LiveTests -c Release --logger "console;verbosity=detailed"
Remove-Item Env:KDS_LIVE_GITHUB_TOKEN
```

See [GitHub token](#github-token) for how to create the token and which permissions it needs.

| Variable | Default | Meaning |
|----------|---------|---------|
| `KDS_LIVE_GITHUB_TOKEN` | — (test skipped) | fine-grained: *Actions* RW, *Secrets* RW, *Metadata* R; classic: `repo`, `workflow` |
| `KDS_LIVE_REPOSITORY` | `kolod/kee-droid-sign` | target repository |
| `KDS_LIVE_REF` | `main` | ref the workflow runs on |
| `KDS_LIVE_TIMEOUT_MINUTES` | `20` | maximum wait |

> **Note:** every live run replaces the repository's signing secrets with a new throwaway key.
> Never point it at a repository whose secrets hold a real release key.

Build the sample app locally (Android SDK and JDK 21 required):

```powershell
cd android
./gradlew :app:assembleDebug
```

## License

KeeDroidSign is free software: you can redistribute it and/or modify it under the terms of the
GNU General Public License as published by the Free Software Foundation, either version 2 of the
License, or (at your option) any later version (`GPL-2.0-or-later`). See [LICENSE](LICENSE).

This matches the license of KeePass itself. Third-party components:

| Component | License | Distributed with the plugin |
|-----------|---------|-----------------------------|
| [KeePass](https://keepass.info/) (submodule, reference only) | GPL-2.0-or-later | no |
| [BouncyCastle.Cryptography](https://www.bouncycastle.org/) | MIT | yes |
| [Sodium.Core](https://github.com/ektrah/libsodium-core) / libsodium (tests only) | MIT / ISC | no |
