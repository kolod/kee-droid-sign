# Research: KeePass Plugin User Interface

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-10-08

Facts below were verified in the `keepass/` submodule (KeePass 2.61.1) and on the development
machine.

## R1. How KeePass discovers and loads the plugin

> **Superseded for distribution (2026-10-08, constitution v1.4.0):** the plugin ships as a single
> `KeeDroidSign.plgx` built by `build/Build-Plgx.ps1`; KeePass compiles it with the .NET Framework
> C# compiler (C# 5 only), so `src/KeeDroidSign` uses `LangVersion` 5. `KeeDroidSign.Core.dll` and
> `BouncyCastle.Cryptography.dll` are packed into the PLGX as `HintPath` references. The DLL
> facts below remain true and are still used for development builds and tests.

- **Facts** (`KeePass/Plugins/PluginManager.cs`): KeePass scans `Plugins/**/*.dll`, skips every file
  whose file-version **ProductName ≠ "KeePass Plugin"** (`AppDefs.PluginProductName`), and creates
  the type `<FileName>.<FileName>Ext` with `Activator.CreateInstanceFrom` (LoadFrom context, so
  dependencies are resolved from the plugin's own folder).
- **Decision**: Plugin assembly `KeeDroidSign.dll`, root namespace `KeeDroidSign`, entry class
  `KeeDroidSign.KeeDroidSignExt : KeePass.Plugins.Plugin`, `<Product>KeePass Plugin</Product>` set
  in the plugin project only (overrides `Directory.Build.props`). Deployed as
  `Plugins/KeeDroidSign/{KeeDroidSign.dll, KeeDroidSign.Core.dll, BouncyCastle.Cryptography.dll}`;
  the two dependency DLLs are ignored by the scanner (different ProductName).
- **Alternatives considered**: `.plgx` (compiled by KeePass at runtime; cannot carry the
  BouncyCastle binary dependency cleanly); ILRepack into one DLL (extra build tool, not needed).

## R2. Compiling against KeePass with the official identity

- **Problem**: Plugins must reference `KeePass, Version=2.61.1.0, PublicKeyToken=fed2ed7716aecf5c`
  (the official, verified on the installed KeePass). The submodule ships dummy signing keys
  (`ReadMe_PFX.txt`), so a normal build produces a different identity and the plugin would not bind
  to the real KeePass.
- **Facts**: `keepass/Ext/PublicKeys/KeePass.pk` is the official public key; its token computes to
  `fed2ed7716aecf5c` (verified). Building `KeePass/KeePass_N48.csproj` with Visual Studio MSBuild
  and `/p:PublicSign=true /p:AssemblyOriginatorKeyFile=<…>/Ext/PublicKeys/KeePass.pk` produced
  `KeePass, Version=2.61.1.0, PublicKeyToken=fed2ed7716aecf5c` (verified); only the optional
  `sgen` step failed, which `/p:GenerateSerializationAssemblies=Off` disables.
- **Decision**: Script `build/Build-KeePassReference.ps1` builds the submodule this way into
  `artifacts/keepass-ref/KeePass.exe` (git-ignored, no changes to the submodule). The plugin and its
  tests reference `$(KeePassReference)`, defaulting to that path; the build fails with a clear
  message pointing to the script if it is missing. Public-signed assemblies load fine in tests
  because .NET Framework skips strong-name verification for full-trust assemblies.
- **Alternatives considered**: Referencing the installed `KeePass.exe` (not reproducible, absent in
  CI); downloading the official zip in CI (network dependency, needs hash pinning). Both remain a
  manual override via `/p:KeePassReference=…`.

## R3. Adding tabs to the Options and entry dialogs

- **Facts**: KeePass has no dedicated API for plugin tabs. `KeePass.UI.GlobalWindowManager` exposes
  the public events `WindowAdded`/`WindowRemoved` with the `Form`. `OptionsForm` and `PwEntryForm`
  both contain a `TabControl` named `m_tabMain`; `PwEntryForm` exposes `EntryRef` publicly;
  `OptionsForm.m_btnOK` has `DialogResult.OK`.
- **Decision**: On `WindowAdded`, if the form is `OptionsForm` / `PwEntryForm`, locate
  `m_tabMain` via `Controls.Find("m_tabMain", true)` and append a `TabPage` hosting our
  `UserControl`. Options are saved when the form closes with `DialogResult.OK`
  (`FormClosed` handler). If the control is not found (future KeePass change), the tab is skipped
  and nothing else breaks. This is the established technique used by popular KeePass plugins.
- **Constitution**: uses only public KeePass types plus the designer control name; recorded in
  Complexity Tracking because the control name is not a formal API.

## R4. Database operations (KeePassLib)

- **Decision**:
  - Groups: find/create `<root>` under `pd.RootGroup`, then `<package id>` (`PwGroup.FindCreateGroup`).
  - Entries: `PwEntry` with `PwDefs.TitleField`, `PasswordField` (`ProtectedString(true, …)`),
    `UrlField`; attachment `entry.Binaries.Set("<package id>.jks", new ProtectedBinary(true, bytes))`.
  - Markers (spec FR-004): custom string fields `DroidSign.Role` = `keystore` | `key` and, on key
    entries, `DroidSign.KeyNumber` = `n`. The alias used in the keystore is `DroidSign.KeyNumber`;
    the title is initialised to the same number. A title that no longer matches shows a warning
    on the tab, nothing breaks (spec clarification Q1 = alias equals the number).
  - Before replacing the keystore attachment: `entry.CreateBackup(pd)` (history, FR-008/US3-4).
  - After changes: `pd.Modified = true` and `host.MainWindow.UpdateUI(false, null, true, group,
    true, null, true)` (FR-009); the plugin never saves the file.
- **Testability**: all of this lives in `DroidSignStore` (no WinForms), tested against an in-memory
  `PwDatabase` (`new PwDatabase(); pd.New(IOConnectionInfo.FromPath("mem.kdbx"), new CompositeKey())`
  — creates the structure without writing a file).

## R5. Adding a key to an existing keystore (core extension)

- **Decision**: New core API `KeystoreEditor.AddKeyAsync(byte[] content, KeyRequest request,
  CancellationToken)` (`request.StorePassword` must be the existing keystore password) — loads with `JksStore`, rejects an existing alias,
  generates the key like `KeystoreGenerator`, saves with the same store password, returns the new
  content and the new key's certificate. `JksStore.Load` without key passwords keeps the existing
  key entries in their encrypted form, so untouched keys need no passwords; tests verify every
  existing key still opens with its own password afterwards (SC-004). Plus `KeystoreReader.ListKeyAliases(content,
  storePassword)`.
- **Rationale**: Feature 001 generates a fresh keystore only; US3 needs "add to file".

## R6. GitHub token from a KeePass entry

- **Decision**: Settings store the token entry's UUID (hex). At export the plugin searches the
  **database that contains the key entry** (`pd.RootGroup.FindEntry(uuid, true)`), reads
  `PasswordField` and builds `GitHubCredential`. The settings tab offers **Select entry…** (a small
  picker listing entries of the active database with group path) and **Clear**. Missing/unknown UUID
  → export disabled with explanation (FR-013).

## R7. Clipboard and dialogs

- **Decision**: Fingerprints are copied with `ClipboardUtil.Copy(text, false, true, entry, pd,
  IntPtr.Zero)` (KeePass clipboard auto-clear applies). Messages use `MessageService.ShowWarning` /
  `ShowInfo`; overwrite confirmation uses `MessageService.AskYesNo` listing secret names only.

## R8. Settings persistence

- **Decision**: `host.CustomConfig` keys prefixed `KeeDroidSign.`: `RootGroup` (default
  `DroidSign`), `TokenEntryUuid`, `KeySize` (4096), `ValidityYears` (30), `PasswordLength` (32),
  `Secret.KeystoreBase64`, `Secret.StorePassword`, `Secret.KeyAlias`, `Secret.KeyPassword`
  (defaults from feature 001). `PluginSettings` (no WinForms) loads/validates/saves through a small
  `ISettingsStore` interface so tests can use a dictionary.

## R9. Responsiveness and cancellation

- **Decision**: Key generation and export run with `async`/`await` from the forms; buttons are
  disabled while running; a **Cancel** button triggers the `CancellationToken` (FR-007). RSA 4096
  generation takes a few seconds, so the window shows a progress indicator.

## R10. Localization

- **Decision**: All UI strings in `src/KeeDroidSign/Properties/Strings.resx` (English, neutral
  culture) accessed through the generated `Strings` class (FR-017); translations can be added as
  `Strings.<culture>.resx` later (Constitution I localization exception).

## R11. Continuous integration

- **Decision**: Add `.github/workflows/dotnet.yml` on `windows-latest`: checkout with submodules →
  `build/Build-KeePassReference.ps1` → `dotnet build` → `dotnet test` (live test skipped). The
  repository currently has no CI for the .NET solution; the constitution's merge gate requires a
  successful build and tests.
