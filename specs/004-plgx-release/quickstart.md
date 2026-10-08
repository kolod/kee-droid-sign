# Quickstart: Validating PLGX Release Publishing

**Feature**: [spec.md](./spec.md) | **Contract**: [contracts/release-workflow.md](./contracts/release-workflow.md)

## 1. Local checks (before merging)

```powershell
./build/Build-KeePassReference.ps1
dotnet build KeeDroidSign.sln -c Release -p:Version=1.0.0
dotnet test  KeeDroidSign.sln -c Release --no-build
powershell -File build/Build-Plgx.ps1 -Version 1.0.0    # expect "Verified: version is 1.0.0.0"
powershell -File build/Build-Plgx.ps1 -Version 9.9.9    # also works: staged copy only
git status                                              # AssemblyInfo.cs unchanged
```

Lint: `actionlint .github/workflows/release.yml`.

## 2. Negative checks on GitHub (optional, after merge)

- Run **Release** manually with `tag` = a non-existent or invalid tag → fails at validation.

## 3. Publish 1.0.0 (after merge, confirmed with the maintainer)

```powershell
git checkout main; git pull
git tag -s v1.0.0 -m "KeeDroidSign 1.0.0"
git push origin v1.0.0
gh run watch            # Release workflow
gh release view v1.0.0  # one asset: KeeDroidSign.plgx; title 1.0.0; marked latest
```

## 4. Install check

Download `KeeDroidSign.plgx` from the release, install (`build/Install-Plugin.ps1` or copy to
`Plugins/`), start KeePass → **Tools → Plugins** shows KeeDroidSign **1.0.0.0**.
