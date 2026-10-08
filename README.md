# KeeDroidSign

A [KeePass 2.x](https://keepass.info/) plugin for managing Android app signing keys:

- generates an Android signing key and a JKS keystore and stores them in the KeePass database;
- exports the keystore and its credentials to a GitHub repository as GitHub Actions secrets;
- shows the SHA-256 certificate fingerprint (`B2:71:2B:...`) required by the Android Developer
  Console.

> **Status:** the plugin (Tools menu, entry tab, Options tab) and the core library are implemented
> and tested; KeePass 2.61.x on Windows.

## Installation

1. Get `KeeDroidSign.plgx`: download the `KeeDroidSign-plgx` artifact of the **.NET build and
   test** workflow, or build it (see [Build and test](#build-and-test)).
2. Close KeePass and copy `KeeDroidSign.plgx` into `<KeePass>/Plugins/`
   (or run `./build/Install-Plugin.ps1` from an elevated PowerShell).
3. Start KeePass. It compiles the plugin on the first start (this takes a few seconds);
   **Tools → Plugins** then lists *KeeDroidSign*.

The PLGX contains the plugin sources plus the prebuilt `KeeDroidSign.Core.dll` and
`BouncyCastle.Cryptography.dll`; KeePass compiles it against the installed KeePass version.
Requirements: KeePass 2.61 or newer, .NET Framework 4.8.

## Usage

- **Tools → DroidSign → New signing key…** — enter the app's package ID, display name, GitHub
  repository and certificate owner. Passwords and the key are generated and stored in the database.
- **Tools → DroidSign → Add key to existing app…** — adds another private key (e.g. a new upload key)
  to the app's `.jks`; the previous file stays in the entry history.
- Open a key entry (`1`, `2`, …) → **DroidSign** tab — display name, package ID and SHA-256 fingerprint with **Copy**
  (for the Android Developer Console) and **Export to GitHub** (the four Actions secrets below).
- **Tools → Options → DroidSign** — root group, GitHub token entry, key defaults, secret names.

After a key is created or added, KeePass saves the database right away (its normal save:
synchronization and triggers apply). To save manually instead, clear **Save the database
automatically after a key is created or added** in **Tools → Options → DroidSign**.

### Database layout

```text
DroidSign/                              root group (configurable)
└── com.example.app/                    one group per package ID
    ├── Example App                     keystore entry: Password = keystore password,
    │                                   URL = GitHub repository, attachment com.example.app.jks
    ├── 1                               key entry: Password = key password, alias "1"
    └── 2                               another key in the same .jks, alias "2"
```

The entries carry `DroidSign.*` custom fields so the plugin recognises them even if you rename
them; the key number in those fields is the alias inside the `.jks`.

## Repository layout

| Path | Contents |
|------|----------|
| `src/KeeDroidSign/` | The KeePass plugin: menu, dialogs, tabs, database layout |
| `src/KeeDroidSign.Core/` | UI- and KeePass-independent core library (.NET Framework 4.8) |
| `tests/KeeDroidSign.Tests/` | Plugin tests against in-memory KeePass databases |
| `tests/KeeDroidSign.Core.Tests/` | Core tests (run offline) |
| `build/` | `Build-KeePassReference.ps1`, `Build-Plgx.ps1`, `Install-Plugin.ps1`, `Update-Strings.ps1` |
| `tests/KeeDroidSign.LiveTests/` | Opt-in end-to-end test against real GitHub (skipped without a token) |
| `android/` | "Hello, World!" Kotlin app used only as a signing test fixture |
| `.github/workflows/android-sign.yml` | Builds and signs the sample APK from the repository secrets |
| `keepass/` | KeePass source code as a git submodule (read-only reference) |
| `specs/` | Feature specifications, plans and task lists |
| `.specify/memory/constitution.md` | Project principles |

## Build and test

Requirements: Windows, .NET Framework 4.8 runtime, .NET SDK 8.0 or newer, Visual Studio or the
Visual Studio Build Tools (MSBuild, for the KeePass reference).

```powershell
git submodule update --init
./build/Build-KeePassReference.ps1   # once: builds keepass/ as the plugin's compile-time reference
dotnet build KeeDroidSign.sln -c Release
dotnet test  KeeDroidSign.sln -c Release
powershell -File build/Build-Plgx.ps1   # Windows PowerShell 5.1: artifacts/plgx/KeeDroidSign.plgx
```

The KeePass sources in the submodule come with dummy signing keys. The script builds them
*public-signed* with the official KeePass public key (`keepass/Ext/PublicKeys/KeePass.pk`), so the
plugin references the official identity `KeePass, PublicKeyToken=fed2ed7716aecf5c` and loads into
stock KeePass. The submodule itself is never modified.

KeePass compiles a PLGX with the .NET Framework's built-in C# compiler, which supports **C# 5
only**. The plugin project (`src/KeeDroidSign`) therefore uses `<LangVersion>5</LangVersion>`, and
`Build-Plgx.ps1` compiles the staged sources with that same compiler before packing. The core
library ships as a prebuilt DLL inside the PLGX and may use newer C#.

User-visible strings live in `src/KeeDroidSign/Properties/Strings.resx`; after editing it, run
`build/Update-Strings.ps1` to regenerate `Strings.cs` (a test fails if they disagree).

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
  the token in the *Password* field, and select that entry in **Tools → Options → DroidSign**. Only a
  reference to the entry is kept in the KeePass configuration; the token is read from the database
  at export time and never stored anywhere else.
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
