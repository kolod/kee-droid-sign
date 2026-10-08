# Contract: Plugin user interface

## Tools menu (`GetMenuItem(PluginMenuType.Main)`)

```text
Tools
└── DroidSign
    ├── New signing key…            (enabled: active database open and unlocked)
    └── Add key to existing app…    (enabled: same, and at least one app group exists)
```

## New signing key window

| Field | Default | Validation |
|-------|---------|------------|
| Package ID | — | Android application ID (data-model.md) and no existing app group |
| Display name | — | required |
| Repository | — | `owner/name` or GitHub URL |
| Common name (CN) | display name | required |
| OU, O, L, ST, C | empty | as feature 001 `DistinguishedName` |
| Key size / validity | from settings | as settings |
| Passwords | generated on **Create** (length from settings) | not shown in the window (a preview would differ from the stored values); visible afterwards in the KeePass entries |

Buttons: **Create** (async, progress, disabled while running), **Cancel** (cancels generation or
closes). On an existing package ID: message offering **Add key** instead (spec US1-3).

## Add key window

Pick app group (list of `<package id> — <display name>`), owner fields prefilled from the newest
key's certificate subject, key size/validity from settings. Shows the number the new key will get.

## Entry dialog tab "DroidSign" (key entries only)

| Element | Content |
|---------|---------|
| Alias / Key number | `n` (+ warning icon if the title differs) |
| Owner | certificate subject (RFC 4514) |
| Valid | `yyyy-MM-dd` – `yyyy-MM-dd` |
| Display name | title of the keystore entry + **Copy** |
| Package ID | `<package id>` (group name) + **Copy** |
| SHA-256 | `AA:BB:…` + **Copy** |
| Repository | `https://github.com/<owner>/<name>` link built from the parsed URL (opens in the system browser; plain text if the URL is not a GitHub repository), with **Export to GitHub** on the same row |
| **Export to GitHub** | enabled when repository, attachment and token entry are available; otherwise disabled with the reason as tooltip/label |

Export flow: plan → if conflicts, `AskYesNo` listing names to overwrite → export → result list
(`name: Created/Updated/Failed (reason)`).

## Options dialog tab "DroidSign"

Root group (editable drop-down of the active database's first-level groups, recycle bin excluded, plus a **Create group** button that adds the typed name at the top level when it does not exist yet; the database is marked modified and the main window refreshed), GitHub token entry (**Select…**, **Clear**, shows title + group path), key size,
validity, password length, defaults for new keys (owner CN/OU/O/L/ST/C and GitHub owner, pre-filled in the New signing key window), four secret names. Validation errors shown inline; saved only when the
Options dialog closes with OK.

## Messages

All texts from `Strings.resx`; never include passwords, token, or keystore bytes.
