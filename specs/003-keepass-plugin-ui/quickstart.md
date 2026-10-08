# Quickstart: Validating the KeePass Plugin

**Feature**: [spec.md](./spec.md) | **UI**: [contracts/plugin-ui.md](./contracts/plugin-ui.md) |
**Layout**: [data-model.md](./data-model.md)

## Prerequisites

- Windows, .NET SDK 8+, Visual Studio 2022+ / Build Tools (MSBuild for the KeePass reference build)
- KeePass 2.61.x installed (for manual UI checks), a **test** database (never a real one)
- Submodule initialised: `git submodule update --init`

## 1. Build

```powershell
./build/Build-KeePassReference.ps1        # once: builds keepass/ with the official public key
dotnet build KeeDroidSign.sln -c Release
dotnet test  KeeDroidSign.sln -c Release   # core + plugin service tests; live test skipped
```

Expected: build without warnings; all tests pass. `artifacts/keepass-ref/KeePass.exe` reports
`PublicKeyToken=fed2ed7716aecf5c`.

## 2. Install into KeePass (manual)

Run `powershell -File build/Build-Plgx.ps1`, copy `artifacts/plgx/KeeDroidSign.plgx` to
`<KeePass>/Plugins/` (or `build/Install-Plugin.ps1`), restart KeePass (first start compiles it).
Expected: **Tools → Plugins** lists *KeeDroidSign*; **Tools → DroidSign** exists.

## 3. Scenario checks

| Story | Steps | Expected |
|-------|-------|----------|
| US4 | Tools → Options → DroidSign; set root group `Android Keys`, select a token entry, OK; reopen | values kept; KeePass.config.xml contains only names/UUID/numbers |
| US1 | Tools → DroidSign → New signing key: `com.example.app`, "Example", `kolod/kee-droid-sign`, CN | group `Android Keys/com.example.app` with keystore entry (attachment, URL, password) and entry `1`; database marked modified |
| US1 | Repeat with the same package ID | refused, offers Add key |
| US2 | Open entry `1` → DroidSign tab | alias 1, owner, validity, SHA-256; Copy puts value on clipboard (auto-cleared) |
| US2 | Compare SHA-256 with `keytool -list -v` of the saved attachment | identical |
| US2 | Export to GitHub (test repository) | conflicts listed and confirmed; per-secret results; `gh secret list` shows 4 secrets |
| US3 | Tools → DroidSign → Add key → `com.example.app` | entry `2`; attachment updated; history of keystore entry has the previous version; keys 1 and 2 open |
| SC-006 | Export key 1, run **Sign sample APK** workflow | run succeeds; summary fingerprint equals the tab's SHA-256 |

## 4. Negative checks

- No database open → menu items disabled.
- Token entry not selected → Export disabled with explanation.
- Edit title of entry `1` to `upload` → tab shows a warning, export still uses alias `1`.
