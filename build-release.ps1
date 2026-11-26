<#
.SYNOPSIS
    Builds a self-contained single-file Release build of XerahS.App for Windows.

.DESCRIPTION
    Produces artifacts\release\XerahS.exe, which runs without an installed .NET
    runtime. build-windows.cmd stays the Debug/iteration script; this one is for
    shippable builds.

    Three things this script does that a plain `dotnet publish` does not:

      * stops a running XerahS.exe, which locks bin\ DLLs and makes MSBuild fail
        with MSB3027 "could not copy ... exceeded the retry count";
      * puts the per-user SDK (%LOCALAPPDATA%\Microsoft\dotnet) first on PATH,
        because publish shells out to `dotnet publish` for the watch-folder daemon
        and omaxerahs, and the machine-wide SDK may be too old to build net11.0
        (the nested call then dies with MSB4216/MSB4027 and the outer publish
        appears to hang for a long time);
      * disables app-driven plugin and web-UI builds, matching build-windows.cmd,
        so the build neither needs node/npm nor fills the disk with plugin output.

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER RuntimeIdentifier
    Target runtime identifier. Default: win-x64.

.PARAMETER Output
    Publish directory. Default: <repo>\artifacts\release.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\build-release.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\build-release.ps1 -Output C:\programs\XerahS-release
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$Output = (Join-Path $PSScriptRoot 'artifacts\release')
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'src\desktop\app\XerahS.App\XerahS.App.csproj'
$outputPath = [System.IO.Path]::GetFullPath($Output)

if (-not (Test-Path -LiteralPath $project)) {
    throw "Project not found: $project"
}

Write-Host '=== stopping running XerahS instances ==='
Get-Process -Name XerahS -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

$perUserDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (Test-Path -LiteralPath $perUserDotnet) {
    $env:Path = (Split-Path -Parent $perUserDotnet) + ';' + $env:Path
    $dotnet = $perUserDotnet
} else {
    $dotnet = 'dotnet'
}

Write-Host '=== publishing ==='
& $dotnet publish $project `
    -c $Configuration `
    -r $RuntimeIdentifier `
    -o $outputPath `
    -m:1 `
    -p:nodeReuse=false `
    -p:UseSharedCompilation=false `
    -p:EnableAppDrivenPluginBuild=false `
    -p:SkipBundlePlugins=true

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $outputPath 'XerahS.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Publish finished but $exe was not produced"
}

$sizeMb = [math]::Round((Get-Item -LiteralPath $exe).Length / 1MB)
Write-Host ''
Write-Host "BUILD OK: $exe ($sizeMb MB)"
Write-Host 'Note: Resources\, PaddleOCR\ and soft_oal.dll must stay next to the exe.'
