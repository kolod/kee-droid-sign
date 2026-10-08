# Data Model: KeePass Plugin User Interface

**Feature**: [spec.md](./spec.md) | **Date**: 2026-10-08

## Database layout (KeePass)

```text
<RootGroup>/                                  PwGroup, name from settings (default "DroidSign")
└── <PackageId>/                              PwGroup, e.g. "io.github.kolod.whiskergrid"
    ├── Keystore entry                        PwEntry
    │     Title            = display name
    │     Password         = keystore password            (protected)
    │     URL              = https://github.com/<owner>/<repo>
    │     Attachment       = "<PackageId>.jks"             (protected binary)
    │     DroidSign.Role   = "keystore"                    (custom field)
    ├── Key entry "1"                         PwEntry
    │     Title            = "1"
    │     Password         = private key password          (protected)
    │     DroidSign.Role   = "key"
    │     (the title "1" is the alias "1" inside the .jks)
    └── Key entry "2" …
```

### Validation rules

| Item | Rule |
|------|------|
| RootGroup | non-empty, no `/` |
| PackageId | `^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$`, ≤ 255 chars |
| Display name | non-empty, ≤ 100 chars |
| Repository URL | parsable by `RepositoryTarget.Parse` (feature 001) |
| Key number (key entry title) | positive integer; next = max(existing titles, aliases in .jks) + 1; a key entry with a non-numeric title is ignored with a warning |
| Keystore entries per app group | exactly 1 (more → first used + warning) |

### State transitions

```text
(none) --New signing key--> App group { keystore entry + key "1" }
App group --Add key--> keystore attachment replaced (old version in entry history) + key "n+1"
```

## PluginSettings (KeePass CustomConfig, prefix `KeeDroidSign.`)

| Key | Type | Default | Validation |
|-----|------|---------|------------|
| `RootGroup` | string | `DroidSign` | non-empty, no `/` |
| `TokenEntryUuid` | hex string | empty | 32 hex chars or empty |
| `KeySize` | int | 4096 | 2048 / 3072 / 4096 |
| `ValidityYears` | int | 30 | 25–100 |
| `PasswordLength` | int | 32 | 8–128 |
| `SaveAfterKeyChange` | bool | true | `true`/`false`; save through KeePass after a key is created or added |
| `Secret.KeystoreBase64` | string | `ANDROID_KEYSTORE_BASE64` | GitHub secret name rules |
| `Secret.StorePassword` | string | `ANDROID_KEYSTORE_PASSWORD` | 〃 |
| `Secret.KeyAlias` | string | `ANDROID_KEY_ALIAS` | 〃 |
| `Secret.KeyPassword` | string | `ANDROID_KEY_PASSWORD` | 〃 |
| `Default.CommonName` | string | empty (= app display name) | ≤ 64 chars |
| `Default.OrganizationalUnit` / `Default.Organization` | string | empty | ≤ 64 chars |
| `Default.Locality` / `Default.State` | string | empty | ≤ 128 chars |
| `Default.Country` | string | empty | 2 ASCII letters |
| `Default.GitHubOwner` | string | empty | GitHub user/organization name; pre-fills the repository field as `https://github.com/owner/` (`https://github.com/` when empty) and the package ID as `io.github.<owner>.` (lower-case, `-` → `_`) |

No secrets are ever stored here (FR-015, Constitution III).

## In-memory models (plugin, no WinForms)

| Type | Purpose |
|------|---------|
| `NewAppRequest` | package ID, display name, repository URL, `DistinguishedName`, key size, validity |
| `AppKeystore` | resolved app group: group, keystore entry, key entries, parsed `RepositoryTarget` |
| `KeyEntryInfo` | key entry, number/alias, title mismatch flag |
| `KeyDetails` | alias, subject, NotBefore/NotAfter, SHA-256 (for the entry tab) |
