# Quickstart: Validate Environment Secrets

## Q1. Automated tests (every build)

```powershell
dotnet test KeeDroidSign.sln -c Release
powershell -File build/Build-Plgx.ps1   # C# 5 compile check of the plugin sources
```

Expected: all tests pass, including the new ones for scopes, environment status, protection,
cleanup, settings default (new vs upgrade), override parsing and confirmation texts. Existing
repository-level export tests pass unchanged (SC-005).

## Q2. Plugin, manual (KeePass)

1. `build/Install-Plugin.ps1`; Options → DroidSign: **GitHub environment** shows `release` on a
   fresh KeePass config, empty on an existing 1.0.x config.
2. Open a key entry of an app whose repository has no `release` environment → Export → error
   "Environment release does not exist…", nothing written.
3. **Protect environment…** with an export-only token → manual steps + missing Administration
   permission; with an admin token → environment created, policies `main` (branch) and `v*` (tag)
   visible under Settings → Environments.
4. Export → confirmation names `owner/repo, environment release`; secrets appear under the
   environment, not under repository secrets.
5. With old repository-level copies present → after export the cleanup question lists them; Yes →
   gone from repository secrets.
6. **Change…** → Repository level → Export goes to repository secrets (1.0.1 behaviour).

## Q3. This repository (maintainer, each step confirmed first)

1. Create/protect environment `release` in `kolod/kee-droid-sign` (plugin action or GitHub UI).
2. Export a throwaway key into it (`KDS_LIVE_ENVIRONMENT=release`, live test export part, or plugin).
3. Merge the PR → the push to `main` runs "Sign sample APK" with `environment: release` → success.
4. Live test:
   ```powershell
   $env:KDS_LIVE_GITHUB_TOKEN = '<token>'   # Environments RW, Actions RW, Metadata R
   dotnet test tests/KeeDroidSign.LiveTests -c Release
   ```
   Expected: pass in < 20 min, fingerprints equal (SC-004).
5. Delete the four repository-level secrets (plugin cleanup or `gh secret delete`).
6. Dispatch "Sign sample APK" from any non-`main` branch → the `sign` job fails with a deployment
   protection rejection; no secret is exposed (SC-003).
