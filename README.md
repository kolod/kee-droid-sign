# KeeDroidSign

A [KeePass 2.x](https://keepass.info/) plugin for managing Android app signing keys:

- generates an Android signing key and a JKS keystore and stores them in the KeePass database;
- exports the keystore and its credentials as GitHub Actions secrets, by default into a protected
  GitHub environment (`release`) so that only the signing job can read them;
- shows the SHA-256 certificate fingerprint (`B2:71:2B:...`) required by the Android Developer
  Console.

> **Status:** the plugin (Tools menu, entry tab, Options tab) and the core library are implemented
> and tested; KeePass 2.61.x on Windows.

## Installation

1. Download `KeeDroidSign.plgx` from the
   [latest release](https://github.com/kolod/kee-droid-sign/releases/latest)
   (or build it yourself, see [Build and test](#build-and-test)).
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
  (for the Android Developer Console) and **Export to GitHub**, which opens the export wizard:
  1. **Target** — the default from the settings, repository secrets, or another environment for
     this app (stored in the keystore entry's `DroidSign.ExportTarget` field).
  2. **Review** — the plugin checks the repository and lists what it found (does the environment
     exist and which branches/tags may use it, is the default branch protected, can anyone create
     `v*` tags, which secrets will be created or overwritten, old repository-level copies) and
     proposes actions: create/restrict the environment (with selectable branch/tag patterns), export
     the secrets, delete the repository-level copies.
  3. **Results** — **Run** executes the selected actions and shows what happened. Nothing on GitHub
     changes before **Run**.
- **Tools → Options → DroidSign** — root group, GitHub token entry, key defaults, secret names and
  the default **GitHub environment**.

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

The entries carry a `DroidSign.Role` custom field so the plugin recognises them. A key entry's
title is its number and the alias inside the `.jks`, so do not rename key entries.

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
| `.github/workflows/android-sign.yml` | Builds and signs the sample APK with the secrets of the `release` environment |
| `.github/workflows/dotnet.yml` | Builds, tests and packages the plugin (PLGX artifact) |
| `.github/workflows/release.yml` | Publishes a GitHub Release with `KeeDroidSign.plgx` for a `vX.Y.Z` tag |
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

## GitHub environment

Repository-level secrets can be read by every workflow on every branch. A GitHub *environment*
limits its secrets to jobs that declare it (`environment: release`) **and** run from a branch or tag
its deployment policy allows. KeeDroidSign therefore exports into an environment by default.

- **Default:** `release` for new installations. Installations upgraded from 1.0.x keep exporting
  repository secrets until you set **Tools → Options → DroidSign → GitHub environment**; an empty
  value means repository secrets. Each app can override the default on the export wizard's
  **Target** page.
- **The environment must exist.** Exporting never creates or changes environments. If it is
  missing, the wizard proposes **Create environment**; otherwise create it on GitHub: repository →
  **Settings → Environments → New environment** `release` → **Deployment branches and tags: Selected
  branches and tags** → add your **default branch** (e.g. `main` or `master`) and `v*` (tag).
  Optionally add required reviewers.
- **Create / restrict environment** in the wizard does the same through the API, proposing your
  repository's real default branch and `v*`; untick what you do not want. It needs the
  *Administration* permission, which also allows changing other repository settings, so grant it
  only if you want this convenience (a separate token for it is fine). Existing reviewers, wait
  timer and branch patterns are kept.
- **An environment only protects as much as the refs it allows.** The wizard warns when:
  - the environment allows every branch;
  - the default branch is not protected (no ruleset requiring pull requests or restricting updates,
    no branch protection) — anyone with write access could push a workflow there that reads the key;
  - creating `v*` tags is not restricted by a tag ruleset — anyone with write access could tag any
    commit, including one with a malicious workflow.
  Fix these in the repository's **Settings → Rules → Rulesets** (or drop the `v*` pattern if you
  sign only from the default branch).
- **Old repository-level copies:** when exporting into an environment, the wizard proposes deleting
  repository secrets with the same names (they stay readable by every workflow); they are deleted
  only after the export succeeded.
- **Private repositories** need GitHub Pro, Team or Enterprise for environments; on GitHub Free use
  repository secrets (wizard **Target** page → *Repository secrets*).

## Core capabilities

| Area | Entry point |
|------|-------------|
| Passwords | `Passwords.PasswordGenerator` — CSPRNG, never produces `$`, backtick, `\`, quotes or whitespace |
| Keystore | `Keystore.KeystoreGenerator` — RSA 4096 / SHA-256 / 30 years, JKS, fully in memory |
| Fingerprint | `Fingerprints.CertificateFingerprint` — SHA-256 or SHA-1, `AA:BB:...` format |
| GitHub | `GitHub.GitHubClient`, `GitHub.SecretExporter`, `GitHub.RepositoryInspector`, `GitHub.EnvironmentProtector` — repository/environment secrets, repository inspection (default branch, rulesets, environment), cleanup of repository copies, environment protection |

Secrets are encrypted with the repository's or environment's public key (libsodium sealed box)
before they leave the machine. Existing secrets are overwritten only after explicit confirmation.

## GitHub Actions contract

The export writes these secrets into the environment (or the repository; names are configurable):

| Secret | Value |
|--------|-------|
| `ANDROID_KEYSTORE_BASE64` | keystore file, single-line Base64 |
| `ANDROID_KEYSTORE_PASSWORD` | keystore password |
| `ANDROID_KEY_ALIAS` | key alias |
| `ANDROID_KEY_PASSWORD` | key password |

Example workflow steps that consume them (as in `.github/workflows/android-sign.yml`). The signing
job must declare the environment; it then receives the secrets only when it runs from a ref the
environment allows (e.g. the default branch or a `v*` tag). Build pull requests without signing (as the
sample workflow's `build-debug` job does).
Pass secrets through `env:` — never write `${{ secrets.X }}` inside a `run:` script, where a value
containing `$(...)`, backticks or quotes would be executed as shell code:

```yaml
  sign:
    if: github.event_name != 'pull_request'
    runs-on: ubuntu-latest
    environment: release
    steps:
      # ... checkout, JDK, Gradle ...
      - name: Decode signing keystore
        env:
          KEYSTORE_BASE64: ${{ secrets.ANDROID_KEYSTORE_BASE64 }}
        run: printf '%s' "$KEYSTORE_BASE64" | base64 -d > app/release.keystore.jks

      - name: Write keystore.properties
        env:
          STORE_PASSWORD: ${{ secrets.ANDROID_KEYSTORE_PASSWORD }}
          KEY_ALIAS: ${{ secrets.ANDROID_KEY_ALIAS }}
          KEY_PASSWORD: ${{ secrets.ANDROID_KEY_PASSWORD }}
        run: |
          # .properties treats '\' as an escape character and drops leading spaces of a value.
          esc() {
            local v="${1//\\/\\\\}"
            if [ "${v:0:1}" = " " ]; then v="\\$v"; fi
            printf '%s' "$v"
          }
          {
            echo "storeFile=release.keystore.jks"
            echo "storePassword=$(esc "$STORE_PASSWORD")"
            echo "keyAlias=$(esc "$KEY_ALIAS")"
            echo "keyPassword=$(esc "$KEY_PASSWORD")"
          } > app/keystore.properties
```

Passwords generated by the plugin contain no shell or `.properties` special characters, so they
also work with simpler consumers.

## Protecting the signing key in CI

Repository secrets can be read by **any workflow that runs on the repository's branches** — anyone
who can push a branch, a merged malicious pull request or a compromised third-party action could
send the key elsewhere. To limit the damage:

- **Use Google Play App Signing.** Google keeps the app signing key; the key you export is only
  the *upload key*, which Google support can reset if it leaks. Never put the app signing key of a
  self-distributed app into CI unless you accept this risk.
- **Protect `main`**: require pull requests and reviews, so workflow changes cannot reach the
  secrets unreviewed.
- **Pin third-party actions by commit SHA** (this repository does; Dependabot proposes updates).
- **Keep the secrets in a GitHub environment** limited to the protected default branch and `v*` (the plugin's default,
  see [GitHub environment](#github-environment)) and use `environment: release` in the signing job.
  Add required reviewers if every signing run should need an approval.
- **Run signing only where needed** (tags or manual dispatch), never on pull requests from forks
  (GitHub withholds secrets from them by default).

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
   | Environments | Read and write | exporting into an environment (default) |
   | Actions | Read-only | reading which branches/tags may use the environment (otherwise "could not be checked"); Read and write for the end-to-end test (start the workflow, download artifacts) |
   | Secrets | Read and write | exporting repository secrets, finding and deleting old repository-level copies |
   | Contents | Read-only | optional: checking classic branch protection (rulesets are read with Metadata) |
   | Metadata | Read-only | selected automatically; default branch, rulesets |
   | Administration | Read and write | **only** for **Create / restrict environment** in the wizard — leave it out otherwise |

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
2. exports it into this repository's `release` environment (the four secrets above);
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
| `KDS_LIVE_GITHUB_TOKEN` | — (test skipped) | fine-grained: *Environments* RW, *Actions* RW, *Metadata* R (*Secrets* RW for `KDS_LIVE_ENVIRONMENT=-`); classic: `repo`, `workflow` |
| `KDS_LIVE_REPOSITORY` | `kolod/kee-droid-sign` | target repository |
| `KDS_LIVE_ENVIRONMENT` | `release` | environment the secrets are exported into; `-` = repository secrets |
| `KDS_LIVE_REF` | `main` | ref the workflow runs on |
| `KDS_LIVE_TIMEOUT_MINUTES` | `20` | maximum wait |

> **Note:** every live run replaces the signing secrets with a new throwaway key.
> Never point it at a repository whose secrets hold a real release key.

Build the sample app locally (Android SDK and JDK 21 required):

```powershell
cd android
./gradlew :app:assembleDebug
```

## Releasing

Releases are published by the **Release** workflow (`.github/workflows/release.yml`):

1. Raise the version in `Directory.Build.props` and `src/KeeDroidSign/Properties/AssemblyInfo.cs`
   (a test checks they match) and merge to `main`.
2. Tag the merge commit and push the tag:

   ```powershell
   git tag -s v1.2.3 -m "KeeDroidSign 1.2.3"
   git push origin v1.2.3
   ```

The workflow builds and tests that commit on Windows, stamps the tag version into the PLGX, and
publishes release `1.2.3` with `KeeDroidSign.plgx` as its only asset and generated release notes.
Tags with a suffix (`v1.3.0-beta.1`) become pre-releases. Tags on commits outside `main` are
refused. An existing release is never recreated; to replace its PLGX, run the workflow manually
with the tag and **replace_asset** checked.

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
