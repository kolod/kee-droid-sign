<#
.SYNOPSIS
    Installs KeeDroidSign.plgx into KeePass: closes KeePass, copies the plugin, starts KeePass again.

.DESCRIPTION
    1. Closes all running KeePass instances with "KeePass.exe --exit-all" (a normal exit: KeePass
       asks about unsaved changes). If an instance is still running after -ExitTimeoutSeconds, the
       script stops, unless -Force is given, which kills the remaining processes (unsaved changes
       are lost).
    2. Removes leftovers of earlier DLL-based installs and copies artifacts/plgx/KeeDroidSign.plgx
       (built by build/Build-Plgx.ps1) to <KeePassDir>/Plugins/.
    3. Starts KeePass again through explorer.exe, so it runs with normal user rights even when this
       script runs elevated (writing to Program Files usually needs an elevated PowerShell).
       Use -NoStart to skip this step.
#>
[CmdletBinding()]
param(
    [string]$KeePassDir = (Join-Path $env:ProgramFiles 'KeePass Password Safe 2'),
    [int]$ExitTimeoutSeconds = 60,
    [switch]$Force,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$plgx = Join-Path $repoRoot 'artifacts/plgx/KeeDroidSign.plgx'
$keePassExe = Join-Path $KeePassDir 'KeePass.exe'

if (-not (Test-Path $keePassExe)) {
    throw "KeePass.exe not found in '$KeePassDir'. Pass -KeePassDir."
}
if (-not (Test-Path $plgx)) {
    throw "'$plgx' not found. Run build/Build-Plgx.ps1 first."
}

# 1. Close running KeePass instances.
$running = @(Get-Process -Name 'KeePass' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host "Closing $($running.Count) KeePass instance(s)..."
    & $keePassExe --exit-all

    $deadline = (Get-Date).AddSeconds($ExitTimeoutSeconds)
    while ((Get-Process -Name 'KeePass' -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
    }

    $remaining = @(Get-Process -Name 'KeePass' -ErrorAction SilentlyContinue)
    if ($remaining.Count -gt 0) {
        if (-not $Force) {
            throw "KeePass is still running after $ExitTimeoutSeconds s (maybe waiting for an answer about unsaved changes). Close it and run again, or use -Force to kill it (unsaved changes are lost)."
        }
        Write-Warning 'Killing remaining KeePass processes (-Force); unsaved changes are lost.'
        $remaining | Stop-Process -Force
        $remaining | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    }
}

# 2. Install.
$target = Join-Path $KeePassDir 'Plugins'
New-Item -ItemType Directory -Force -Path $target | Out-Null

# Leftovers of DLL-based installs would load the plugin twice or fail on missing dependencies:
# Plugins/KeeDroidSign/ and loose DLLs directly in Plugins/.
$oldDir = Join-Path $target 'KeeDroidSign'
if (Test-Path $oldDir) {
    Remove-Item -Recurse -Force $oldDir
    Write-Host "Removed old DLL installation: $oldDir"
}
# Only our own DLLs: a loose BouncyCastle.Cryptography.dll may belong to another plugin.
foreach ($name in 'KeeDroidSign.dll', 'KeeDroidSign.Core.dll') {
    $loose = Join-Path $target $name
    if (Test-Path $loose) {
        Remove-Item -Force $loose
        Write-Host "Removed old file: $loose"
    }
}

Copy-Item -Path $plgx -Destination $target -Force
Write-Host "Installed $(Join-Path $target 'KeeDroidSign.plgx')"

# 3. Start KeePass with normal user rights (explorer.exe launches it unelevated).
if (-not $NoStart) {
    Start-Process -FilePath (Join-Path $env:WINDIR 'explorer.exe') -ArgumentList "`"$keePassExe`""
    Write-Host 'Started KeePass. The first start compiles the plugin and takes a few seconds longer.'
}
