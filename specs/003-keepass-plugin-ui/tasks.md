# Tasks: KeePass Plugin User Interface

**Input**: Design documents from `/specs/003-keepass-plugin-ui/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/plugin-ui.md](./contracts/plugin-ui.md),
[contracts/core-additions.md](./contracts/core-additions.md), [quickstart.md](./quickstart.md)

**Tests**: REQUIRED for all non-UI logic (Constitution Principle VI: security-critical paths —
database layout, key addition, token resolution, export). WinForms windows and tabs are verified
manually with [quickstart.md](./quickstart.md). Write tests first and see them fail.

**Organization**: Tasks are grouped by user story so each story can be implemented and verified
independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User story the task belongs to (US1–US4)

## Path Conventions

- Plugin: `src/KeeDroidSign/` (namespaces follow folders: `KeeDroidSign.Settings`, `.Storage`, `.Services`, `.UI`)
- Plugin tests: `tests/KeeDroidSign.Tests/`
- Core additions: `src/KeeDroidSign.Core/`, tests in `tests/KeeDroidSign.Core.Tests/`
- English code and comments; every user-visible string in `src/KeeDroidSign/Properties/Strings.resx`
- Test data uses obviously fake passwords; tests create in-memory `PwDatabase` only

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: KeePass reference build, projects, plugin skeleton that KeePass recognises

- [X] T001 Create `build/Build-KeePassReference.ps1`: locate MSBuild via `vswhere` (`-latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`), build `keepass/KeePass/KeePass_N48.csproj` with `/p:Configuration=Release /p:PublicSign=true /p:AssemblyOriginatorKeyFile=<repo>/keepass/Ext/PublicKeys/KeePass.pk /p:GenerateSerializationAssemblies=Off /p:OutDir=<repo>/artifacts/keepass-ref/ /p:BaseIntermediateOutputPath=<repo>/artifacts/keepass-obj/`, then verify `[Reflection.AssemblyName]::GetAssemblyName(...)` reports `PublicKeyToken=fed2ed7716aecf5c` and `Version=2.61.1.0` (fail otherwise); finally assert `git -C keepass status --porcelain` is empty
- [X] T002 Run `build/Build-KeePassReference.ps1` and confirm `artifacts/keepass-ref/KeePass.exe` exists (`artifacts/` is already git-ignored)
- [X] T003 Create `src/KeeDroidSign/KeeDroidSign.csproj`: SDK-style net48, `UseWindowsForms` true, `AssemblyName`/`RootNamespace` `KeeDroidSign`, `<Product>KeePass Plugin</Product>` (overrides Directory.Build.props, research R1), `GenerateDocumentationFile` with `NoWarn` CS1591, property `KeePassReference` defaulting to `$(MSBuildThisFileDirectory)..\..\artifacts\keepass-ref\KeePass.exe`, `<Reference Include="KeePass"><HintPath>$(KeePassReference)</HintPath><Private>false</Private></Reference>`, a target before build that errors with "KeePass reference not found: run build/Build-KeePassReference.ps1" when the file is missing, project reference to `KeeDroidSign.Core`, `InternalsVisibleTo` `KeeDroidSign.Tests`
- [X] T004 [P] Create `tests/KeeDroidSign.Tests/KeeDroidSign.Tests.csproj` (net48, xUnit 2.9.x, runner, Test SDK, `PlatformTarget` x64, KeePass reference with `Private=true` so tests can load it, project references to `KeeDroidSign` and `KeeDroidSign.Core`) and add both new projects to `KeeDroidSign.sln`
- [X] T005 [P] Create `src/KeeDroidSign/Properties/Strings.resx` (neutral English) with `PublicResXFileCodeGenerator`-style generated accessor `Strings.Designer.cs` (namespace `KeeDroidSign.Properties`); start with `MenuRoot` = "DroidSign", `MenuNewKey` = "New signing key...", `MenuAddKey` = "Add key to existing app..."; later tasks add their strings here
- [X] T006 Create `src/KeeDroidSign/KeeDroidSignExt.cs`: `public sealed class KeeDroidSignExt : Plugin` storing `IPluginHost`, `Initialize` returns true, `Terminate` unhooks everything, `GetMenuItem(PluginMenuType.Main)` returns a "DroidSign" item with the two sub-items (handlers are placeholders showing "Not implemented yet" via `MessageService.ShowInfo`), sub-items enabled only when `host.MainWindow.ActiveDatabase.IsOpen` (update in `DropDownOpening`)
- [X] T007 Build the solution and verify `src/KeeDroidSign/bin/Release/net48/KeeDroidSign.dll` has ProductName "KeePass Plugin" (`(Get-Item ...).VersionInfo.ProductName`) and that `KeeDroidSign.Core.dll` and `BouncyCastle.Cryptography.dll` are next to it and `KeePass.exe` is NOT copied

**Checkpoint**: Solution builds; plugin DLL is recognisable by KeePass

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Settings, entry field names, database layout reading, and dialog tab injection used by every story

### Tests ⚠️ write first, must fail

- [X] T008 [P] Write `tests/KeeDroidSign.Tests/Settings/PluginSettingsTests.cs` using a dictionary-backed `ISettingsStore`: defaults per data-model.md; round-trip save/load; `Validate()` rejects empty root group, root group containing `/`, key size 1024, validity 24, password length 7, invalid/duplicate secret names (reuse `SecretNames`), malformed token UUID; saved keys never contain values other than names/numbers/UUID
- [X] T009 [P] Create `tests/KeeDroidSign.Tests/Support/TestDatabase.cs`: helper creating an in-memory `PwDatabase` (`new PwDatabase(); pd.New(IOConnectionInfo.FromPath("test.kdbx"), new CompositeKey())`) and helpers to build an app group by hand (keystore entry with attachment generated by `KeystoreGenerator` RSA 2048, key entries) for read-side tests
- [X] T010 [P] Write `tests/KeeDroidSign.Tests/Storage/DroidSignStoreReadTests.cs`: `ListApps` finds app groups only under the configured root (case-sensitive name, created nowhere by reads); `FindApp` returns null when absent; with two keystore entries in one group the first is used and `AppKeystore.Warnings` mentions it; `IsKeyEntry` true only for `DroidSign.Role=key`; `ResolveKey` returns keystore bytes, store password, alias from `DroidSign.KeyNumber`, key password and repository; title ≠ number sets `TitleMismatch`; missing attachment/URL reported as typed problems, not exceptions

### Implementation

- [X] T011 [P] Create `src/KeeDroidSign/Settings/ISettingsStore.cs`, `CustomConfigSettingsStore.cs` (wraps `host.CustomConfig.GetString/SetString`, prefix `KeeDroidSign.`) and `PluginSettings.cs` (properties, defaults, `Load`, `Validate`, `Save`, `ToSecretMapping()`) per data-model.md
- [X] T012 [P] Create `src/KeeDroidSign/Storage/EntryFields.cs` (constants `Role` = "DroidSign.Role", `KeyNumber` = "DroidSign.KeyNumber", role values `keystore`/`key`, attachment name pattern `<package id>.jks`) and `src/KeeDroidSign/Storage/Models.cs` (`NewAppRequest`, `AppKeystore`, `KeyEntryInfo`, `KeyContext`, `KeyDetails`, `KeyProblem` enum) per data-model.md; secret-holding types redact in `ToString()`
- [X] T013 Implement read side of `src/KeeDroidSign/Storage/DroidSignStore.cs`: constructor `(PwDatabase, PluginSettings)`, `ListApps`, `FindApp`, static `IsKeyEntry`, `ResolveKey`, package ID validation helper (regex from data-model.md); never creates groups when only reading
- [X] T014 Create `src/KeeDroidSign/UI/DialogTabInjector.cs`: subscribes to `GlobalWindowManager.WindowAdded`; for a given form type predicate finds `m_tabMain` via `form.Controls.Find("m_tabMain", true)`, appends a `TabPage` with a provided `UserControl`, and exposes the form to the factory; silently does nothing if the control is missing; `Dispose` unsubscribes (called from `KeeDroidSignExt.Terminate`)
- [X] T015 Run plugin tests: T008 and T010 green

**Checkpoint**: Foundation ready — stories can start

---

## Phase 3: User Story 1 - Create a signing key for a new app (Priority: P1) 🎯 MVP

**Goal**: Tools → DroidSign → New signing key creates the app group, keystore entry and key `1`

**Independent Test**: `dotnet test --filter "FullyQualifiedName~CreateApp"`; manually quickstart §3 rows US1

### Tests for User Story 1 ⚠️ write first, must fail

- [X] T016 [P] [US1] Write `tests/KeeDroidSign.Tests/Storage/DroidSignStoreCreateTests.cs`: `CreateApp` creates `<root>/<package id>` (creating root when missing), keystore entry (Title, protected Password, URL `https://github.com/<owner>/<repo>`, protected attachment `<package id>.jks` equal to keystore bytes, `DroidSign.Role=keystore`) and key entry `1` (protected Password, `Role=key`, `KeyNumber=1`); sets `pd.Modified`; refuses an existing app group (`InvalidOperationException`); rejects invalid package IDs (`com`, `1abc.def`, `a..b`, `with space.x`)
- [X] T017 [P] [US1] Write `tests/KeeDroidSign.Tests/Services/KeyServiceCreateTests.cs`: `CreateAppAsync` generates passwords of the configured length, alias `1`, key size/validity from settings; the stored keystore opens with the keystore entry password and key `1` with the key entry password (via `KeystoreReader.Open`); cancellation leaves the database unchanged; no exception text contains the generated passwords (`SecretLeakScanner` copy or equivalent)

### Implementation for User Story 1

- [X] T018 [US1] Implement `DroidSignStore.CreateApp(NewAppRequest, SigningKeystore)` in `src/KeeDroidSign/Storage/DroidSignStore.cs` per data-model.md (protected strings/binaries, markers, `pd.Modified = true`)
- [X] T019 [US1] Implement `src/KeeDroidSign/Services/KeyService.cs` `CreateAppAsync(NewAppRequest, CancellationToken)`: validate, refuse existing app, generate store and key passwords (`PasswordGenerator`, length from settings), build `KeyRequest` (alias "1"), `KeystoreGenerator.GenerateAsync`, then `store.CreateApp`; database changes only after generation succeeded
- [X] T020 [US1] Create `src/KeeDroidSign/UI/NewKeyForm.cs` (+ `NewKeyForm.Designer.cs`) per contracts/plugin-ui.md: fields, CN defaulting to display name, note that passwords are generated on Create (no preview: it would differ from the stored values), Create (async, progress bar, controls disabled), Cancel (cancels token / closes), inline validation messages from `Strings.resx`; on existing package ID shows a message offering Add key
- [X] T021 [US1] Wire **New signing key…** in `src/KeeDroidSign/KeeDroidSignExt.cs`: open `NewKeyForm` for the active database; after success call `host.MainWindow.UpdateUI(false, null, true, appGroup, true, null, true)` and select the new key entry
- [X] T022 [US1] Run US1 tests green; add all US1 strings to `Strings.resx`

**Checkpoint**: MVP — keys can be created and stored in the database layout

---

## Phase 4: User Story 2 - Export secrets and view the fingerprint from the entry dialog (Priority: P1)

**Goal**: DroidSign tab in the entry dialog with key details, fingerprints + copy, Export to GitHub

**Independent Test**: `dotnet test --filter "FullyQualifiedName~Describe|FullyQualifiedName~ExportService"`; manually quickstart §3 rows US2

### Tests for User Story 2 ⚠️ write first, must fail

- [X] T023 [P] [US2] Write `tests/KeeDroidSign.Tests/Services/KeyServiceDescribeTests.cs`: `Describe(KeyContext)` returns alias, subject `CN=…`, NotBefore/NotAfter and SHA-256/SHA-1 equal to `CertificateFingerprint.Compute` of the alias's certificate; wrong store password / missing alias produce typed problems
- [X] T024 [P] [US2] Write `tests/KeeDroidSign.Tests/Services/ExportServiceTests.cs` with the feature-001 `FakeGitHubHandler` approach (copy a minimal fake handler into `tests/KeeDroidSign.Tests/Support/`): `ResolveToken` returns null when the UUID is empty/unknown and the entry's Password otherwise (searching the given database); `PlanAsync`/`ExportAsync` send the four secrets with names from settings and values = keystore Base64, store password, alias `n`, key password of that key (decrypt with `Sodium.Core` test dependency as in feature 001, or assert via a stub exporter); no secret in results/exceptions

### Implementation for User Story 2

- [X] T025 [US2] Implement `KeyService.Describe(KeyContext)` in `src/KeeDroidSign/Services/KeyService.cs` using `KeystoreReader` and `CertificateFingerprint`
- [X] T026 [US2] Implement `src/KeeDroidSign/Services/ExportService.cs` (`ResolveToken`, `PlanAsync`, `ExportAsync`) over `GitHubClient` + `SecretExporter` with `PluginSettings.ToSecretMapping()` and an optional `HttpMessageHandler` for tests
- [X] T027 [US2] Create `src/KeeDroidSign/UI/EntryTabControl.cs` (+ Designer) per contracts/plugin-ui.md: shows alias/number (warning icon + tooltip on title mismatch), owner, validity, SHA-256 and SHA-1 with **Copy** buttons using `ClipboardUtil.Copy(text, false, true, entry, pd, IntPtr.Zero)`, repository, **Export to GitHub** (disabled with reason when repository/attachment/token missing); export flow: plan → `MessageService.AskYesNo` listing names to overwrite → export (async, button disabled) → result list; errors via `MessageService.ShowWarning` without secrets
- [X] T028 [US2] Register the entry-dialog tab in `src/KeeDroidSign/KeeDroidSignExt.cs` via `DialogTabInjector` for `PwEntryForm` when `DroidSignStore.IsKeyEntry(form.EntryRef)`; database = `host.MainWindow.DocumentManager.FindContainerOf(entry)` or `ActiveDatabase`
- [X] T029 [US2] Run US2 tests green; add US2 strings to `Strings.resx`

**Checkpoint**: Fingerprint and export available from any key entry

---

## Phase 5: User Story 3 - Add another key to an existing app keystore (Priority: P2)

**Goal**: Add a private key to the existing `.jks` with alias `n+1` and a new key entry

**Independent Test**: `dotnet test --filter "FullyQualifiedName~KeystoreEditor|FullyQualifiedName~AddKey"`; manually quickstart §3 row US3

### Tests for User Story 3 ⚠️ write first, must fail

- [X] T030 [P] [US3] Write `tests/KeeDroidSign.Core.Tests/Keystore/KeystoreEditorTests.cs`: `AddKeyAsync` on a keystore with alias `1` adds alias `2`; both keys open with their own passwords; key `1`'s certificate bytes unchanged; existing alias → `ArgumentException`; wrong store password → `InvalidKeystorePasswordException`; `ListKeyAliases` returns `["1","2"]`; optional keytool interop check that keytool lists two `PrivateKeyEntry` items (`[KeytoolFact]`)
- [X] T031 [P] [US3] Write `tests/KeeDroidSign.Tests/Services/KeyServiceAddKeyTests.cs`: `AddKeyAsync` picks `max(KeyNumber, aliases) + 1` (also with gaps `1,3` → `4`); keystore entry history gains one item holding the previous attachment; attachment replaced; new key entry created; all previous keys still open; failure (wrong store password in entry) leaves the database untouched (no history item, no new entry)

### Implementation for User Story 3

- [X] T032 [US3] Implement `src/KeeDroidSign.Core/Keystore/KeystoreEditor.cs` (`AddKeyAsync`) and add `ListKeyAliases` to `src/KeeDroidSign.Core/Keystore/KeystoreReader.cs` per contracts/core-additions.md, reusing the key/certificate generation of `KeystoreGenerator` (extract a shared internal helper rather than duplicating)
- [X] T033 [US3] Implement `DroidSignStore.AddKey(AppKeystore, SigningKeystore, int)` in `src/KeeDroidSign/Storage/DroidSignStore.cs`: `keystoreEntry.CreateBackup(pd)`, replace attachment, touch modification time, create key entry `n`, `pd.Modified = true`
- [X] T034 [US3] Implement `KeyService.AddKeyAsync(AppKeystore, DistinguishedName, CancellationToken)` in `src/KeeDroidSign/Services/KeyService.cs`: next number, new key password, `KeystoreEditor.AddKeyAsync`, then `store.AddKey`
- [X] T035 [US3] Create `src/KeeDroidSign/UI/AddKeyForm.cs` (+ Designer): app list (`<package id> — <display name>`), owner fields prefilled from the newest key's certificate subject, shows the new number, async Create with progress, Cancel; wire **Add key to existing app…** in `KeeDroidSignExt.cs` (disabled when no app exists) and the offer from `NewKeyForm` (US1-3)
- [X] T036 [US3] Run US3 tests green; add US3 strings to `Strings.resx`

**Checkpoint**: Key rotation supported

---

## Phase 6: User Story 4 - Plugin settings (Priority: P2)

**Goal**: DroidSign tab in Options with root group, token entry, key defaults and secret names

**Independent Test**: settings tests (T008) green; manually quickstart §3 row US4

### Implementation for User Story 4

- [X] T037 [P] [US4] Create `src/KeeDroidSign/UI/EntryPickerForm.cs` (+ Designer): list of entries of the active database (title + group path, protected fields never shown), filter box, OK/Cancel, returns the selected `PwUuid`
- [X] T038 [US4] Create `src/KeeDroidSign/UI/OptionsTabControl.cs` (+ Designer): binds `PluginSettings` fields, token entry shown as "title (group path)" or "not selected", **Select…**/**Clear**, numeric inputs with ranges, inline validation using `PluginSettings.Validate()`
- [X] T039 [US4] Register the Options tab in `src/KeeDroidSign/KeeDroidSignExt.cs` via `DialogTabInjector` for `OptionsForm`; on `FormClosing` with `DialogResult.OK` validate (cancel closing and select the tab on error) and save through `CustomConfigSettingsStore`; Cancel discards
- [X] T040 [US4] Make `KeyService`, `ExportService` and the forms read settings at operation time (no cached copy), so changed settings apply to the next operation (US4-2); add US4 strings to `Strings.resx`

**Checkpoint**: All stories implemented

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T041 [P] Create `.github/workflows/dotnet.yml` (research R11): `windows-latest`, checkout with `submodules: true`, setup .NET SDK, run `build/Build-KeePassReference.ps1`, `dotnet build KeeDroidSign.sln -c Release`, `dotnet test KeeDroidSign.sln -c Release --no-build`; triggers push to main and pull requests; `permissions: contents: read`; lint with actionlint
- [X] T042 [P] Create `build/Install-Plugin.ps1`: copies `KeeDroidSign.dll`, `KeeDroidSign.Core.dll`, `BouncyCastle.Cryptography.dll` from `src/KeeDroidSign/bin/<Configuration>/net48/` to `<KeePassDir>/Plugins/KeeDroidSign/` (parameter `-KeePassDir`, default `$env:ProgramFiles\KeePass Password Safe 2`), refusing to run while KeePass is running
- [X] T043 [P] Update `README.md`: status (plugin usable), installation, usage (menu, entry tab, options), database layout diagram, building (reference script) and the token entry setting
- [X] T044 Review against the constitution: English-only (non-ASCII scan of `src/KeeDroidSign`), `git -C keepass status` clean, no secrets in settings (inspect saved `KeePass.config.xml` keys during quickstart), all strings in `Strings.resx` (grep forms for string literals shown to users)
- [X] T045 Run full `dotnet test KeeDroidSign.sln -c Release`; then execute quickstart §1–4 in a real KeePass with a test database (manual UI verification), including SC-006 end-to-end export + workflow run; record results in this file's notes

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** → **Foundational (Phase 2)** → stories
- **US1**: needs Phase 2
- **US2**: needs Phase 2 (tests build app groups via `TestDatabase`, not via US1); manual check needs US1 to create data
- **US3**: needs Phase 2; reuses US1's `KeyService` file (sequential edits to `KeyService.cs`/`DroidSignStore.cs`)
- **US4**: needs Phase 2 (settings model); independent of US1–US3
- **Polish**: after all stories

### Story Graph

```text
Setup → Foundational ─┬─> US1 ─> US3
                      ├─> US2
                      └─> US4 ──────────> Polish
```

### Within Each Story

Tests (fail) → storage/core → service → UI → wiring in `KeeDroidSignExt` → tests green.

## Parallel Opportunities

- Phase 1: T004 ‖ T005 after T003
- Phase 2: T008 ‖ T009 ‖ T010, then T011 ‖ T012
- US1: T016 ‖ T017
- US2: T023 ‖ T024
- US3: T030 ‖ T031; T032 (core) ‖ T033 (store)
- US4 can be developed in parallel with US1–US3 (different files except `KeeDroidSignExt.cs` registration)
- Polish: T041 ‖ T042 ‖ T043

### Parallel Example: User Story 3

```text
Task: "Write KeystoreEditorTests in tests/KeeDroidSign.Core.Tests/Keystore/KeystoreEditorTests.cs"
Task: "Write KeyServiceAddKeyTests in tests/KeeDroidSign.Tests/Services/KeyServiceAddKeyTests.cs"
```

## Implementation Strategy

### MVP First

1. Setup + Foundational
2. US1 → keys are created in the database (usable with KeePass alone)
3. US2 → fingerprint and export (completes the core user value)

### Incremental Delivery

4. US3 → key rotation
5. US4 → settings tab (until then defaults are used)
6. Polish → CI, install script, README, manual verification

## Notes

- Never write the database file from the plugin; KeePass saves it
- Test databases only for manual checks; never point the live export at a repository holding real secrets
- Commit after each phase

## Implementation notes (2026-10-08)

- Dialogs are built in code with `TableLayoutPanel` (DPI-friendly) instead of separate
  `*.Designer.cs` files; strings still come from `Properties/Strings.resx`.
- New-key window: no password preview (it would differ from the values generated on Create);
  passwords are visible afterwards in the KeePass entries. `contracts/plugin-ui.md` updated.
- Options tab: invalid values are not saved and a warning is shown, but the Options dialog still
  closes, because KeePass has already applied its own settings when OK is pressed.
- Tests: xUnit shadow copying is disabled for `KeeDroidSign.Tests`, otherwise .NET Framework
  rejects the public-signed KeePass reference (strong-name bypass applies only to assemblies in the
  application folder).
- Added automated UI smoke tests (`tests/KeeDroidSign.Tests/UI/`): plugin naming/menu, tab
  injection logic, and a guard that KeePass's `PwEntryForm`/`OptionsForm` contain `m_tabMain`.
- T045: automated part done (all tests green); manual verification in a running KeePass confirmed
  by the maintainer on 2026-10-08 after the PLGX fixes (AssemblyProduct attribute in source).
- Distribution switched to a single `KeeDroidSign.plgx` (constitution v1.4.0): the plugin project
  was rewritten in C# 5 (`LangVersion` 5) because KeePass compiles PLGX sources with the .NET
  Framework compiler; `Strings.cs` is now committed (generated by `build/Update-Strings.ps1`,
  checked by `StringsTests`); `build/Build-Plgx.ps1` verifies the sources with that compiler and
  runs `KeePass.exe --plgx-create`; CI uploads the PLGX; `Install-Plugin.ps1` copies the PLGX.
