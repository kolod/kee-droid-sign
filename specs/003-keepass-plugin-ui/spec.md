# Feature Specification: KeePass Plugin User Interface

**Feature Branch**: `003-keepass-plugin-ui`

**Created**: 2026-10-08

**Status**: Draft

**Input**: User description: "Now build the plugin itself: the user interface. The settings store the
KeePass database group in which new keys are created. Storage structure:
`/DroidSign/<app package id>/<jks file password>` + the jks file itself attached + repository URL
as URL + display name as name; `/DroidSign/<app package id>/<private key password>` (there may be
several if the jks file contains several private keys) + name = number. New keys can be generated
for the same app; they are added to the file. An additional tab in the key's properties adds
buttons for saving the secrets; there one can also see the digest of the public key in the format
Google expects." (original request was written in Ukrainian; translated per Constitution
Principle I)

## Scope Note

Features 001 and 002 built and proved the core: passwords, in-memory JKS keystores, fingerprints
and GitHub secret export. This feature turns the core into a usable KeePass plugin: a settings tab,
a key-generation window opened from the **Tools** menu (Constitution Principle II), a fixed layout
for keys inside the KeePass database, and an extra tab in the entry dialog for exporting secrets
and showing the certificate fingerprint.

## Clarifications

### Session 2026-10-08

- Q: How is a private key's alias inside the keystore recorded, given the key entry's title is its
  number? → A: The alias inside the JKS file is the number itself: key entry `1` ↔ alias `1`,
  entry `2` ↔ alias `2`. Nothing else is stored.
- Q: Where does the plugin get the GitHub personal access token? → A: From a KeePass entry
  chosen in the plugin settings: the settings keep only a reference to that entry (its UUID), the
  token is read from the entry's Password field at export time.

## Database Layout

The root group is configurable in the plugin settings (default `DroidSign`).

```text
<root group>/                         e.g. DroidSign
└── <app package id>/                 group, e.g. io.github.kolod.whiskergrid
    ├── <keystore entry>              Title = display name of the app
    │                                 Password = keystore (JKS) password
    │                                 URL = GitHub repository URL
    │                                 Attachment = the .jks file
    ├── 1                             key entry: Title = key number
    │                                 Password = private key password
    │                                 (the title is also the key's alias in the .jks file)
    ├── 2                             (one entry per private key in the .jks file)
    └── ...
```

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create a signing key for a new app (Priority: P1)

The developer opens **Tools → DroidSign → New signing key**, enters the app package ID, a display
name, the GitHub repository URL and the certificate owner details. The plugin generates strong
passwords and a keystore with one private key and stores everything in the database under
`<root>/<package id>/` in the layout above.

**Why this priority**: It is the plugin's primary purpose; every other story operates on what this
one creates.

**Independent Test**: With an open database, create a key for `com.example.app`; the database
contains the group, the keystore entry with the `.jks` attachment, password, URL and title, and a
key entry `1` whose password opens the private key in that keystore.

**Acceptance Scenarios**:

1. **Given** an open, unlocked database, **When** the developer fills in the form and confirms,
   **Then** the group `<root>/<package id>` is created with one keystore entry and one key entry `1`
   exactly as described in Database Layout, and the database is marked as modified.
2. **Given** the created entries, **When** the keystore is opened with the keystore entry's password
   and the key with key entry `1`'s password, **Then** both succeed.
3. **Given** a package ID group that already contains a keystore entry, **When** the developer tries
   to create a new key for that package ID from the "new app" form, **Then** the plugin refuses and
   offers to add a key to the existing keystore instead (User Story 3).
4. **Given** an invalid package ID (not a valid Android application ID) or missing required fields,
   **When** the developer confirms, **Then** the form shows which field is wrong and nothing is
   written to the database.
5. **Given** no database is open or it is locked, **When** the developer opens the menu item,
   **Then** the item is disabled or a message explains that a database must be opened first.

---

### User Story 2 - Export secrets and view the fingerprint from the entry dialog (Priority: P1)

When the developer opens a key entry (e.g. `1`) in KeePass's entry dialog, an extra **DroidSign**
tab shows the certificate's SHA-256 fingerprint in the format Google expects
(`AA:BB:…`), with copy buttons, and a button that exports the four GitHub Actions
secrets for this key to the repository stored in the keystore entry's URL.

**Why this priority**: It delivers the two outputs the developer actually needs — the fingerprint
for the Android Developer Console and the CI secrets.

**Independent Test**: Open key entry `1` created by User Story 1; the tab shows a fingerprint equal
to the one computed independently from the attached keystore; export to a test repository creates
the four secrets.

**Acceptance Scenarios**:

1. **Given** a key entry in a valid DroidSign group, **When** its entry dialog is opened, **Then**
   the DroidSign tab shows the key's alias, certificate owner, validity dates and the SHA-256
   fingerprint, with a copy button.
2. **Given** the repository URL in the keystore entry, **When** the developer clicks
   **Export to GitHub**, **Then** the plugin shows which secrets will be created or overwritten,
   asks for confirmation before overwriting, exports, and shows per-secret results.
3. **Given** an entry that is not a DroidSign key entry, **When** its dialog is opened, **Then** no
   DroidSign tab is added.
4. **Given** the keystore entry has no repository URL or no attachment, **When** the tab is shown,
   **Then** the export button is disabled with an explanation, while the fingerprint is still shown
   if the keystore is available.
5. **Given** the export fails (token invalid, no permission, network), **When** the result is shown,
   **Then** the message explains the cause without revealing any secret.

---

### User Story 3 - Add another key to an existing app keystore (Priority: P2)

For an app that already has a keystore, the developer can generate an additional private key (for
example a new upload key). The new key is added to the same `.jks` file, and a new key entry with
the next number is created.

**Why this priority**: Needed for key rotation and for apps that use several keys; the first key
(User Story 1) covers the common case.

**Independent Test**: Add a key to the app from User Story 1; the attachment now contains two
private keys, a key entry `2` exists, and both keys open with their entries' passwords.

**Acceptance Scenarios**:

1. **Given** an app group with a keystore and key entries `1..n`, **When** the developer chooses
   **Add key** (from the Tools window, or the group/entry context menu), **Then** a new key is
   generated, added to the keystore, the attachment is replaced with the updated file, and key
   entry `n+1` is created.
2. **Given** the updated keystore, **When** all key entries are checked, **Then** every existing key
   is still present and opens with its own password; nothing else in the keystore changed.
3. **Given** the keystore entry's password does not open the attached keystore, **When** adding a
   key, **Then** the operation stops with an error and the database is unchanged.
4. **Given** the update succeeds, **When** the database history is inspected, **Then** the previous
   version of the keystore entry (with the old attachment) is kept in the entry history, so the
   change can be undone.

---

### User Story 4 - Plugin settings (Priority: P2)

A **DroidSign** tab in KeePass's Options dialog holds the plugin's global settings: the root group
name, defaults for new keys (key size, validity, password length) and the default GitHub secret
names.

**Why this priority**: The plugin works with defaults; settings make it adaptable.

**Independent Test**: Change the root group to `Android Keys`, create a key; it is stored under
`Android Keys/<package id>/`. Restart KeePass; the setting is kept.

**Acceptance Scenarios**:

1. **Given** the Options dialog, **When** it is opened, **Then** a DroidSign tab shows the current
   settings with their defaults.
2. **Given** changed settings, **When** the dialog is closed with OK, **Then** they are saved and
   used by the next operation; with Cancel nothing changes.
3. **Given** invalid values (empty root group name, secret name violating GitHub rules), **When**
   OK is pressed, **Then** the tab shows the error and does not save.
4. **Given** any settings, **When** they are saved, **Then** no password, token or key material is
   stored in them (Constitution Principle III).

### Edge Cases

- Two groups for the same package ID exist (e.g. created manually): the plugin uses the first and
  warns.
- A key entry's number has a gap (`1`, `3`): the next key gets the highest number + 1.
- The keystore entry's attachment was replaced by the user with a different file: the tab shows an
  error for keys whose alias is not found.
- The database is saved by KeePass's normal mechanism only; the plugin never writes files itself.
- The root group is renamed or moved by the user: the plugin finds groups by the name configured in
  settings, starting from the database root.
- Package IDs containing characters that KeePass shows specially (dots are allowed and expected).

## Requirements *(mandatory)*

### Functional Requirements

**Database layout**

- **FR-001**: Keys MUST be stored under `<root group>/<package id>/`, where the root group name comes
  from the settings (default `DroidSign`) and is created when missing.
- **FR-002**: Each app group MUST contain exactly one keystore entry: Title = display name,
  Password = keystore password, URL = repository URL, attachment = the `.jks` file named
  `<package id>.jks`.
- **FR-003**: Each private key in the keystore MUST have its own key entry: Title = sequential
  number starting at 1, Password = private key password. The key's alias inside the
  keystore equals the entry's number (`1`, `2`, …).
- **FR-004**: All passwords MUST be stored as protected values; entries MUST carry a plugin marker
  (custom field `DroidSign.Role`) so the plugin recognises keystore and key entries. The key
  number is not duplicated in a field: it is read from the key entry's title, so key entries must
  not be renamed.

**Key generation (Tools menu window)**

- **FR-005**: The plugin MUST add a **DroidSign** submenu to KeePass's **Tools** menu with
  **New signing key…** and **Add key to existing app…**; both are disabled when no database is
  open and unlocked.
- **FR-006**: The new-key window MUST collect: package ID (validated as an Android application ID),
  display name, repository URL (validated as a GitHub repository), certificate owner fields, key
  size and validity (prefilled from settings); passwords are generated with the feature-001
  generator and may be regenerated or shown on request.
- **FR-007**: Generation MUST run without freezing the KeePass window and MUST be cancellable.
- **FR-008**: Adding a key MUST load the existing keystore with the keystore entry's password, add a
  new private key under a new alias, save it with the same keystore password, replace the
  attachment, back up the previous entry state to the entry history, and create the next key entry.
- **FR-009**: After any change the database MUST be marked modified and the KeePass UI refreshed.
  The plugin MUST NOT write the database file itself; it MAY trigger KeePass's own save
  (`MainForm.SaveDatabase`) after a key was created or added, only when the user enabled
  "save after key change" in the settings (on by default).

**Entry dialog tab**

- **FR-010**: The plugin MUST add a **DroidSign** tab to KeePass's entry dialog for key entries
  only.
- **FR-011**: The tab MUST show the key alias, certificate owner, validity period, and the SHA-256
  fingerprint in `AA:BB:…` format (SHA-1 is not shown: the Developer Console uses SHA-256), with
  a copy-to-clipboard button using KeePass's
  clipboard handling (auto-clear).
- **FR-012**: The tab MUST provide **Export to GitHub**, which exports the four secrets
  (`ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`,
  `ANDROID_KEY_PASSWORD`, names from settings) for this key to the repository in the keystore
  entry's URL, using the feature-001 exporter: list conflicts, ask before overwriting, show
  per-secret results.
- **FR-013**: The GitHub token MUST be read at export time from the Password field of the KeePass
  entry selected in the settings (FR-014); only that entry's UUID is stored in the settings. If no
  entry is selected or it no longer exists, export is disabled with an explanation. The token MUST
  never be stored in settings or shown in messages.

**Settings (Options tab)**

- **FR-014**: The plugin MUST add a **DroidSign** tab to KeePass's Options dialog with: root group
  name, the GitHub token entry (picked from the open database; stored as a reference only), default
  key size, default validity (years), default password length, and the four secret names.
- **FR-015**: Settings MUST be validated before saving and persisted in KeePass's configuration;
  they MUST NOT contain secrets.

**General**

- **FR-016**: The plugin MUST load in stock KeePass 2.x and ship as a plugin assembly with its
  dependencies; it MUST NOT require changes to KeePass.
- **FR-017**: All user-visible strings MUST come from resources so they can be localized.
- **FR-018**: Errors MUST be shown as KeePass-style message boxes with a cause and no secret values.

### Key Entities

- **Plugin Settings**: root group name, token entry reference (UUID), key defaults, secret names;
  stored in KeePass configuration.
- **App Group**: a database group named after the package ID under the root group.
- **Keystore Entry**: one per app group; display name, keystore password, repository URL, `.jks`
  attachment, plugin marker.
- **Key Entry**: one per private key; sequential number (= alias in the keystore), key password,
  plugin marker.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer creates a complete signing setup for a new app (key + database entries) in
  under 2 minutes, without leaving KeePass.
- **SC-002**: From an existing key entry, the fingerprint is copied for the Developer Console in at
  most 2 clicks after opening the entry.
- **SC-003**: Exporting secrets for a key takes at most 3 clicks after opening the entry and
  completes in under 15 seconds on a normal connection.
- **SC-004**: Adding a second key leaves 100% of existing keys usable with their stored passwords.
- **SC-005**: No password, token or key material appears in plugin settings, the KeePass
  configuration file, or any message shown by the plugin.
- **SC-006**: A keystore created by the plugin signs the feature-002 sample app successfully
  (end-to-end compatibility).

## Assumptions

- KeePass 2.61.x on Windows; the plugin is distributed as DLL(s) placed in the KeePass `Plugins`
  folder (`.plgx` is not required).
- Adding tabs to the Options and entry dialogs is done by the plugin when those dialogs open;
  stock KeePass provides no dedicated API for this, and the technique is common among KeePass
  plugins. The plan will confirm it is compatible with Constitution Principle II.
- Importing existing keystores created outside the plugin is out of scope for this feature.
- The certificate owner fields default to the display name as common name; other fields are
  optional.
- UI language is English; strings are prepared for localization (FR-017).
