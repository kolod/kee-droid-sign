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

Create a personal access token and store it in KeePass:

- **Fine-grained token (recommended):** repository access to the target repositories with
  *Secrets: Read and write* and *Metadata: Read*.
- **Classic token:** `repo` scope.

The token is used only for HTTPS calls to `api.github.com` and is never written to logs or plugin
settings.

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
