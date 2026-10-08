# Implementation Plan: KeePass Plugin User Interface

**Branch**: `003-keepass-plugin-ui` | **Date**: 2026-10-08 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-keepass-plugin-ui/spec.md`

## Summary

Build the KeePass plugin `KeeDroidSign.dll` on top of `KeeDroidSign.Core`: a **Tools → DroidSign**
menu with "New signing key" and "Add key" windows, a **DroidSign** tab in the entry dialog (key
details, SHA-256 fingerprint with copy, Export to GitHub) and a **DroidSign** tab in Options.
Keys are stored as `<root>/<package id>/` with a keystore entry (password, URL, `.jks` attachment)
and numbered key entries whose number is the alias. The plugin compiles against KeePass built from
the submodule with the official public key, so it loads into stock KeePass. See
[research.md](./research.md).

## Technical Context

**Language/Version**: plugin C# 5 (`LangVersion` 5, required by KeePass's PLGX compiler), core
C# 12; .NET Framework 4.8, WinForms

**Primary Dependencies**: KeePass 2.61.1 (`KeePass.exe` reference built from the submodule, public
signed, R2), `KeeDroidSign.Core` (features 001/002), BouncyCastle.Cryptography (transitive)

**Storage**: KeePass database (groups/entries/attachments via KeePassLib); settings in KeePass
`CustomConfig`

**Testing**: xUnit on net48 for core additions and plugin services against an in-memory
`PwDatabase`; UI verified manually via [quickstart.md](./quickstart.md)

**Target Platform**: Windows, stock KeePass 2.61.x

**Project Type**: Desktop plugin (class library loaded by KeePass)

**Performance Goals**: UI stays responsive; RSA 4096 generation off the UI thread with progress;
export < 15 s (SC-003)

**Constraints**: No secrets in settings/messages/logs; no changes to `keepass/`; plugin never saves
the database file; strings localizable

**Scale/Scope**: 3 windows/tabs + 1 options tab, ~15 classes, tens of apps per database

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Gate | Pre-research | Post-design |
|-----------|------|--------------|-------------|
| I. English-only | Code/docs English; strings in resources | ✅ | ✅ `Strings.resx` (R10) |
| II. Plugin architecture | Plugin API only; Options tab; Tools-menu window; no `keepass/` changes | ⚠️ tabs need a technique | ✅ `Plugin`, `IPluginHost`, `GetMenuItem(Main)`, `GlobalWindowManager` (public); submodule only *built* (R2), never modified. Locating `m_tabMain` by name → Complexity Tracking |
| III. Secrets protected | Protected strings/binaries; no secrets in settings; confirmation before overwrite; clipboard auto-clear | ✅ | ✅ R4, R6 (token read from entry, only UUID stored), R7, overwrite confirmation |
| IV. Scope | Key generation, GitHub export, fingerprint | ✅ | ✅ |
| V. Tooling | No external tools at runtime | ✅ N/A | ✅ N/A (MSBuild only for building the reference) |
| VI. Testable core, thin UI | Logic outside WinForms; tests for security-critical paths | ✅ | ✅ `DroidSignStore`, `KeyService`, `ExportService`, `PluginSettings`, `KeystoreEditor` tested; forms only bind |
| Tech constraints | C#, net48, single PLGX loadable by stock KeePass, plugin in C# 5 (v1.4.0) | ✅ | ✅ `Build-Plgx.ps1` verifies with KeePass's compiler, `KeeDroidSignExt` (R1) |

Result: **PASS** with one justified item (Complexity Tracking).

## Project Structure

### Documentation (this feature)

```text
specs/003-keepass-plugin-ui/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── plugin-ui.md
│   └── core-additions.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
build/
└── Build-KeePassReference.ps1        # MSBuild keepass/ → artifacts/keepass-ref (public sign, R2)
src/
├── KeeDroidSign.Core/
│   └── Keystore/KeystoreEditor.cs    # + KeystoreReader.ListKeyAliases (R5)
└── KeeDroidSign/                     # the plugin
    ├── KeeDroidSign.csproj           # net48, WinForms, Product "KeePass Plugin", ref $(KeePassReference)
    ├── KeeDroidSignExt.cs            # Plugin entry: menu, window hooks, lifetime
    ├── Properties/Strings.resx       # all UI strings
    ├── Settings/
    │   ├── PluginSettings.cs
    │   ├── ISettingsStore.cs
    │   └── CustomConfigSettingsStore.cs
    ├── Storage/
    │   ├── DroidSignStore.cs         # database layout (R4)
    │   ├── EntryFields.cs            # field/marker names
    │   └── Models.cs                 # NewAppRequest, AppKeystore, KeyEntryInfo, KeyContext, KeyDetails
    ├── Services/
    │   ├── KeyService.cs
    │   └── ExportService.cs
    └── UI/
        ├── NewKeyForm.cs             # + Designer
        ├── AddKeyForm.cs
        ├── EntryTabControl.cs        # entry dialog tab
        ├── OptionsTabControl.cs      # Options tab
        ├── EntryPickerForm.cs        # token entry picker
        └── DialogTabInjector.cs      # GlobalWindowManager hook (R3)
tests/
├── KeeDroidSign.Core.Tests/Keystore/KeystoreEditorTests.cs
└── KeeDroidSign.Tests/               # plugin services against in-memory PwDatabase
    ├── KeeDroidSign.Tests.csproj
    ├── Storage/DroidSignStoreTests.cs
    ├── Services/KeyServiceTests.cs
    ├── Services/ExportServiceTests.cs
    └── Settings/PluginSettingsTests.cs
.github/workflows/dotnet.yml          # windows: reference build + dotnet build/test (R11)
```

**Structure Decision**: The plugin is a new project next to the core; all non-UI logic lives in
`Settings/`, `Storage/` and `Services/` so it is testable without WinForms (Constitution VI). A new
test project references the plugin and the KeePass reference assembly. The KeePass reference is a
build artifact, never committed.

## Complexity Tracking

| Item | Why Needed | Simpler Alternative Rejected Because |
|------|------------|-------------------------------------|
| Locating KeePass's `m_tabMain` control by name to add tabs (Principle II "plugin API only") | Spec requires tabs in the entry and Options dialogs; KeePass has no tab API | Separate windows instead of tabs contradict the user's requirement; the lookup is guarded (missing control → tab skipped, nothing breaks) and uses only public KeePass events |
| Building `keepass/` with the official *public* key (R2) | The plugin must bind to the official `KeePass` identity | Referencing an installed KeePass is not reproducible/CI-friendly; dummy-key build cannot load into stock KeePass |
