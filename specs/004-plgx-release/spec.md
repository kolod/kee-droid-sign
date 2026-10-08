# Feature Specification: PLGX Release Publishing

**Feature Branch**: `004-plgx-release`

**Created**: 2026-10-08

**Status**: Draft

**Input**: User description: "Now we need a GitHub Action that publishes a release, and publish
version 1.0.0 — only the plgx file." (original request was written in Ukrainian; translated per
Constitution Principle I)

## Scope Note

Feature 003 already builds `KeeDroidSign.plgx` on every push and pull request, but only as a
short-lived workflow artifact. Users need a permanent, versioned download. This feature adds a
release process that turns a version tag into a public GitHub Release whose only downloadable file
is the PLGX, and uses it to publish the first public version, 1.0.0.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Publish a release from a version tag (Priority: P1)

The maintainer pushes a version tag such as `v1.0.0`. An automated release process builds the
plugin from exactly that commit, verifies it (same checks as the regular build: tests, KeePass
compiler check, plugin product name), and publishes a GitHub Release named after the version with
`KeeDroidSign.plgx` as its only asset and automatically generated release notes.

**Why this priority**: It is the feature itself; without it there is no versioned download.

**Independent Test**: Push a test tag on a fork or a pre-release tag; a release appears with exactly
one asset, `KeeDroidSign.plgx`, whose embedded plugin version equals the tag version.

**Acceptance Scenarios**:

1. **Given** a commit on `main`, **When** the maintainer pushes tag `vX.Y.Z`, **Then** a GitHub
   Release `X.Y.Z` (tag `vX.Y.Z`) is published with exactly one asset, `KeeDroidSign.plgx`, and
   generated release notes listing the merged changes since the previous release.
2. **Given** the release process runs, **When** the build, any test, the KeePass compiler check or
   the product-name check fails, **Then** no release is published and the run shows the failure.
3. **Given** the published PLGX, **When** KeePass loads it, **Then** the plugin reports version
   `X.Y.Z` in **Tools → Plugins**.
4. **Given** a tag with a pre-release suffix (e.g. `v1.1.0-beta.1`), **When** the release is
   published, **Then** it is marked as a pre-release and its numeric version is `1.1.0`.
5. **Given** a tag that is not a valid version (e.g. `v1.0`, `release-1`), **When** it is pushed,
   **Then** the release process does not publish anything.
6. **Given** a release for the tag already exists, **When** the process runs again (re-run),
   **Then** it does not create a duplicate release; the asset is replaced only if the maintainer
   re-runs it deliberately.

---

### User Story 2 - Publish version 1.0.0 (Priority: P1)

After the release process is merged, the maintainer publishes the first public version: the project
version is set to 1.0.0, tag `v1.0.0` is created on `main`, and the release appears with the PLGX.

**Why this priority**: Explicitly requested; it is the first deliverable for users.

**Independent Test**: The repository's Releases page shows `1.0.0` as the latest release with
`KeeDroidSign.plgx`; downloading it into KeePass's `Plugins` folder loads plugin version 1.0.0.

**Acceptance Scenarios**:

1. **Given** the release process is on `main`, **When** tag `v1.0.0` is pushed, **Then** release
   `1.0.0` is published as the latest release with only `KeeDroidSign.plgx`.
2. **Given** the downloaded `KeeDroidSign.plgx`, **When** it is installed in KeePass 2.61, **Then**
   KeePass compiles and loads it and shows version 1.0.0.

### Edge Cases

- The tag points to a commit that is not on `main`: the release is refused (releases come from
  `main` only).
- The tag version and the version recorded in the source disagree: the tag wins for the published
  build, and the mismatch is visible in the run log.
- Two tags pushed at once: each produces its own release; they do not overwrite each other.
- The KeePass reference build or PLGX packaging step hangs (KeePass shows an error dialog): the
  run fails after a bounded time instead of waiting forever.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An automated release process MUST run when a tag matching `v<major>.<minor>.<patch>`
  (optionally followed by `-<pre-release>`) is pushed, and MUST be startable manually for an
  existing tag.
- **FR-002**: The release process MUST build from the tagged commit and run the same verification as
  the regular build: all automated tests, the KeePass compiler compatibility check and the plugin
  product-name check.
- **FR-003**: The plugin version embedded in the published PLGX MUST equal the tag's numeric version
  (`X.Y.Z`, as `X.Y.Z.0` assembly version).
- **FR-004**: The GitHub Release MUST contain exactly one asset, `KeeDroidSign.plgx`; no other build
  outputs (DLLs, symbols, source archives beyond GitHub's automatic ones) are attached.
- **FR-005**: The release title MUST be the version (`X.Y.Z`); release notes MUST be generated
  automatically from the changes since the previous release.
- **FR-006**: Tags with a pre-release suffix MUST produce releases marked as pre-release; other tags
  produce normal releases marked as latest.
- **FR-007**: The release process MUST refuse tags whose commit is not contained in `main`.
- **FR-008**: The release process MUST use the least permissions needed (write access to releases
  only for the publishing step) and MUST NOT require any repository secret.
- **FR-009**: The project version in the source MUST be raised to 1.0.0 before tagging `v1.0.0`, so
  local builds and the release agree.
- **FR-010**: The README MUST tell users to download `KeeDroidSign.plgx` from the latest release.

### Key Entities

- **Version Tag**: `vX.Y.Z[-pre]` on a commit in `main`; the single source of the release version.
- **Release**: GitHub Release `X.Y.Z` with generated notes, pre-release flag, and one asset.
- **Release Asset**: `KeeDroidSign.plgx` carrying plugin version `X.Y.Z.0`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From pushing a version tag to the release being downloadable takes under 10 minutes.
- **SC-002**: 100% of published releases contain exactly one asset, `KeeDroidSign.plgx`.
- **SC-003**: 100% of published PLGX files load in KeePass and report the version of their tag.
- **SC-004**: A failing check prevents publication in 100% of cases (no partial releases).
- **SC-005**: Release 1.0.0 is publicly available on the repository's Releases page.

## Assumptions

- Releases are published by the maintainer pushing tags; there is no automatic version bump.
- KeePass's built-in online update check for plugins (version file at an update URL) is out of
  scope for this feature.
- The PLGX is not code-signed; KeePass compiles it locally, and users download it over HTTPS from
  GitHub.
- The version in source (`Directory.Build.props`, plugin `AssemblyInfo.cs`) is kept in step with
  releases; the tag still overrides it in the release build.
- Release notes come from GitHub's automatic generation (merged pull requests); no hand-written
  changelog file is required.
