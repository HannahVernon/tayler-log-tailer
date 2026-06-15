<#
.SYNOPSIS
    Builds the self-contained publish output and compiles the Inno Setup installer
    for Tayler Log Tailer.

.DESCRIPTION
    Runs `dotnet publish` for the WPF application as a self-contained, x64,
    framework-bundled build, then invokes the Inno Setup command-line compiler
    (ISCC.exe) against installer\TaylerLogTailer.iss to produce a per-machine
    setup executable under the repository's dist\ folder.

    Both the publish output (publish\) and the installer output (dist\) are
    git-ignored build artifacts.

.PARAMETER Configuration
    The MSBuild configuration to publish.  Defaults to Release.

.PARAMETER IsccPath
    Full path to the Inno Setup command-line compiler.  Defaults to the standard
    Inno Setup 6 install location.

.PARAMETER SkipPublish
    When set, skips the dotnet publish step and compiles the installer against
    whatever is already in publish\win-x64.

.EXAMPLE
    .\build-installer.ps1
    Publishes the app and builds the installer.

.EXAMPLE
    .\build-installer.ps1 -SkipPublish
    Rebuilds only the installer from the existing publish output.
#>
[CmdletBinding()]
param (
    [string] $Configuration = 'Release',
    [string] $IsccPath = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$project = Join-Path $repoRoot 'src\TaylerLogTailer\TaylerLogTailer.csproj'
$publishDir = Join-Path $repoRoot 'publish\win-x64'
$issScript = Join-Path $scriptRoot 'TaylerLogTailer.iss'

if (-not $SkipPublish) {
    Write-Host "Publishing $project (self-contained win-x64, $Configuration)..." -ForegroundColor Cyan
    dotnet publish $project `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path (Join-Path $publishDir 'TaylerLogTailer.exe'))) {
    throw "Publish output not found at $publishDir. Run without -SkipPublish first."
}

if (-not (Test-Path $IsccPath)) {
    throw "Inno Setup compiler not found at '$IsccPath'. Install Inno Setup 6 or pass -IsccPath."
}

Write-Host "Compiling installer with ISCC..." -ForegroundColor Cyan
& $IsccPath $issScript
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE."
}

Write-Host "Installer build complete. Output is in $(Join-Path $repoRoot 'dist')." -ForegroundColor Green
