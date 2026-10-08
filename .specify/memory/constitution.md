<!--
Sync Impact Report
==================
Version change: 1.2.0 → 1.3.0 (MINOR: new technology rule)
Modified sections:
  - Technology & Platform Constraints — Android test fixtures (end-to-end signing verification)
    are written in Kotlin in the top-level `android/` folder, never distributed with the plugin
Templates requiring updates: none (plan-template Constitution Check is derived at plan time)
Previous amendment 1.1.1 → 1.2.0 (MINOR: new exception to a MUST rule)
Modified principles:
  - I. English-Only Repository Content — added an exception for test string literals that
    exercise non-English / non-ASCII input; such literals are written as readable characters
  - Development Workflow & Quality Gates — merge gate wording updated for the new exception
Previous amendment 1.1.0 → 1.1.1 (PATCH: clarification):
  - V. Configurable External Tooling — states the preference for in-process implementations;
    tool-path rules apply only if an external tool is used (key generation now needs no Java)
Previous amendment 1.0.0 → 1.1.0 (MINOR: feature scope item redefined):
  - IV. Defined Feature Scope — item 3 changed from "encrypted private key upload for Google
    Play App Signing (PEPK)" to "SHA-256 certificate fingerprint for the Android Developer
    Console" (corrects a misinterpretation of the original request)
  - V. Configurable External Tooling — removed `pepk.jar` from the tool examples
  - VI. Testable Core, Thin UI — "Play Console export" replaced by "certificate fingerprint"
Added sections: none
Removed sections: none
Previous ratification (1.0.0) added principles I–VI and sections Technology & Platform
Constraints, Development Workflow & Quality Gates, Governance.
Templates requiring updates:
  - .specify/templates/plan-template.md ✅ no change needed (Constitution Check gates are
    derived from this file at plan time)
  - .specify/templates/spec-template.md ✅ no change needed
  - .specify/templates/tasks-template.md ✅ no change needed (generic "tests optional" note is
    overridden by Principle VI for security-critical logic; specs MUST request those tests)
  - .specify/templates/checklist-template.md ✅ no change needed
  - .specify/templates/commands/*.md — directory not present, nothing to check
  - README.md — not present yet; MUST be written in English when created (Principle I)
Follow-up TODOs: none
-->

# KeeDroidSign Constitution

## Core Principles

### I. English-Only Repository Content

Every file committed to git — source code, identifiers, comments, commit messages, specs,
plans, tasks, documentation, UI resource defaults, and scripts — MUST be written in English.
The ONLY exceptions are:

- localization files (translation resources for non-English UI languages);
- string literals in test code whose purpose is to exercise handling of non-English or
  non-ASCII input (e.g. a Cyrillic certificate subject). Such literals MUST be written as
  readable characters, not `\uXXXX` escapes; the surrounding identifiers, test names and
  comments remain English.

This rule applies regardless of the language used in a request or conversation: when a request
is made in another language, its content MUST be translated to English before it is committed.

Rationale: a single repository language keeps the project accessible to the wider KeePass and
Android developer community and keeps reviews and tooling consistent.

### II. KeePass Plugin Architecture

The product is a plugin for KeePass 2.x. The KeePass source code is included as the git
submodule `keepass/` and serves as the build reference and API source.

- The plugin MUST integrate only through the KeePass plugin API (`KeePass.Plugins.Plugin`,
  `IPluginHost`, menu/options extension points, `PwDatabase` / `PwEntry` APIs).
- The plugin MUST NOT require modifications to the `keepass/` submodule. Changing the submodule
  is limited to updating its pinned revision, with the reason stated in the commit message.
- The plugin MUST add a settings tab to the KeePass Options dialog holding all global plugin
  settings.
- Key generation and related actions MUST be launched from a dedicated window opened via the
  KeePass **Tools** menu.

Rationale: staying on the public plugin surface keeps the plugin compatible with stock KeePass
releases and makes submodule updates low-risk.

### III. Secrets Never Leave KeePass Unprotected (NON-NEGOTIABLE)

- Generated signing material (keystore, private key, passwords, certificate) MUST be stored in
  the open KeePass database, using protected strings and/or entry attachments.
- Private keys and passwords MUST NOT be written to logs, error messages, settings files, or the
  plugin configuration. Settings MUST NOT contain secrets; GitHub tokens MUST be stored in the
  KeePass database, not in plugin settings.
- Temporary files needed by external tools MUST be created in a private temp location, used for
  the shortest possible time, and deleted (overwritten where feasible) in all code paths,
  including failures and cancellation.
- Exporting to GitHub MUST use HTTPS and the GitHub REST API secret encryption scheme (sealed
  box with the repository public key); plaintext secrets MUST NOT be sent.
- Any destructive or outward-facing action (overwriting an existing key entry, overwriting
  existing GitHub secrets) MUST require explicit user confirmation.

Rationale: an Android app signing key is irrecoverable and high-value; leaking or losing it
compromises every future release of the app.

### IV. Defined Feature Scope

The plugin MUST provide these capabilities, and new features MUST relate to Android app signing
key management:

1. Generate an Android app signing key pair (private key + public certificate in a keystore)
   and store it in the KeePass database.
2. Export the keystore and its credentials to a user-specified GitHub repository as GitHub
   Actions secrets, with configurable secret names.
3. Compute the SHA-256 fingerprint of the signing certificate in the colon-separated hex format
   (e.g. `B2:71:2B:...`) required by the Android Developer Console. The fingerprint is public
   data; the private key itself is never exported for this purpose.

Rationale: a focused scope keeps the attack surface and maintenance cost small.

### V. Configurable External Tooling

- Built-in, in-process implementations are preferred over external tools. If the plugin invokes
  any external tool, its path MUST be configurable in the plugin settings tab; paths MUST NOT be
  hard-coded. Sensible auto-detection (e.g. `JAVA_HOME`, `PATH`) MAY prefill defaults.
- The settings tab MUST validate configured paths and report missing or invalid tools clearly
  before an operation starts.
- External processes MUST be invoked with argument lists (no shell string concatenation), with
  secrets passed via stdin or protected temp files rather than command-line arguments where the
  tool allows it.

Rationale: users have varied JDK and tool installations; explicit configuration avoids silent
failures and command-injection risks.

### VI. Testable Core, Thin UI

- Business logic (key generation orchestration, database entry mapping, GitHub secret
  encryption and upload, certificate fingerprint) MUST live in classes independent of WinForms so it
  can be unit-tested.
- Security-critical logic — secret encryption, temp-file cleanup, entry storage — MUST have
  automated tests. Feature specs touching these areas MUST request those tests.
- UI forms MUST stay thin: collect input, call the core, display results and errors.
- Start simple (YAGNI); every added dependency MUST be justified in the plan.

Rationale: WinForms UI is hard to test; isolating logic keeps the critical paths verifiable.

## Technology & Platform Constraints

- Language: C#, targeting the .NET Framework version supported by the KeePass 2.x build in the
  submodule.
- Android test fixtures — sample apps that exist only to verify end-to-end signing (key
  generation → secret export → CI build and signing) — MUST be written in Kotlin and live in the
  top-level `android/` folder, built with the Gradle wrapper. They are never distributed with the
  plugin, MUST NOT be referenced by the .NET solution, and follow Principles I and III (no secrets
  or signing material committed; `keystore.properties` and keystores are git-ignored).
- Distribution: a plugin DLL (or `.plgx` if adopted) loadable by stock KeePass 2.x on Windows.
- Third-party libraries MUST be compatible with the target framework, permissively licensed,
  and bundled with the plugin; preference is given to the .NET base library and KeePass APIs.
- User-visible strings MUST be kept in resources so they can be localized (localization files
  are exempt from Principle I).

## Development Workflow & Quality Gates

- Work follows the Spec Kit flow: specify → (clarify) → plan → tasks → implement.
- Every plan MUST pass a Constitution Check against Principles I–VI before design starts and
  after design is complete; violations MUST be listed with justification in Complexity Tracking.
- Before merging: the solution builds without errors, automated tests pass, no secrets or
  non-English content (outside localization files and test input literals) are committed, and the `keepass/` submodule
  is unchanged unless intentionally re-pinned.

## Governance

- This constitution supersedes other project practices. Conflicting guidance MUST be resolved in
  favor of this document or by amending it.
- Amendments are made by updating this file with a Sync Impact Report and propagating changes
  to dependent templates in `.specify/templates/`.
- Versioning follows semantic versioning: MAJOR for removing or redefining principles, MINOR for
  adding principles or materially expanding guidance, PATCH for clarifications and wording.
- Reviews of specs, plans, and code MUST verify compliance with these principles.

**Version**: 1.3.0 | **Ratified**: 2026-10-08 | **Last Amended**: 2026-10-08
