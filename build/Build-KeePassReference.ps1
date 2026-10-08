<#
.SYNOPSIS
    Builds KeePass from the keepass/ submodule as a compile-time reference for the plugin.

.DESCRIPTION
    The submodule ships with dummy signing keys, so a normal build produces an assembly identity
    that stock KeePass would not accept. This script public-signs the build with the official
    KeePass public key (keepass/Ext/PublicKeys/KeePass.pk), which yields the official identity
    "KeePass, Version=<x>, PublicKeyToken=fed2ed7716aecf5c". The submodule is never modified.

    The project's post-build sgen step (KeePass.XmlSerializers.dll) is skipped: it needs the
    private key and the plugin does not use it.

    Output: artifacts/keepass-ref/KeePass.exe (git-ignored).
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'keepass/KeePass/KeePass_N48.csproj'
$publicKey = Join-Path $repoRoot 'keepass/Ext/PublicKeys/KeePass.pk'
$outDir = Join-Path $repoRoot 'artifacts/keepass-ref/'
$objDir = Join-Path $repoRoot 'artifacts/keepass-obj/'
$expectedToken = 'fed2ed7716aecf5c'

if (-not (Test-Path $project)) {
    throw "KeePass sources not found at '$project'. Run 'git submodule update --init' first."
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path $vswhere)) {
    throw 'vswhere.exe not found. Install Visual Studio or the Visual Studio Build Tools (MSBuild).'
}
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
    Select-Object -First 1
if (-not $msbuild) {
    throw 'MSBuild not found. Install Visual Studio or the Visual Studio Build Tools.'
}

Write-Host "Building KeePass reference with $msbuild"
& $msbuild $project /nologo /verbosity:minimal /restore:false `
    "/p:Configuration=$Configuration" `
    '/p:PublicSign=true' `
    "/p:AssemblyOriginatorKeyFile=$publicKey" `
    '/p:GenerateSerializationAssemblies=Off' `
    '/p:PostBuildEvent=' `
    "/p:OutDir=$outDir" `
    "/p:BaseIntermediateOutputPath=$objDir"
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE."
}

$exe = Join-Path $outDir 'KeePass.exe'
$name = [Reflection.AssemblyName]::GetAssemblyName($exe)
$token = -join ($name.GetPublicKeyToken() | ForEach-Object { $_.ToString('x2') })
if ($token -ne $expectedToken) {
    throw "Unexpected public key token '$token' (expected '$expectedToken')."
}

$status = git -C (Join-Path $repoRoot 'keepass') status --porcelain
if ($status) {
    throw "The keepass/ submodule was modified by the build:`n$status"
}

Write-Host "OK: $($name.FullName)"
Write-Host "    $exe"
