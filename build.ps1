<#
.SYNOPSIS
    Restores, builds, and optionally publishes and packages Tayler Log Tailer.

.DESCRIPTION
    One entry point for building the app from the repository root.  By default it
    restores and builds the Tayler.slnx solution; warnings are treated as errors
    via Directory.Build.props, so a clean build reports 0 warnings / 0 errors.

    Optional switches produce distributable artifacts:
      -Publish    self-contained, framework-bundled win-x64 build under publish\
      -Installer  the Inno Setup installer under dist\ (delegates to
                  installer\build-installer.ps1)

    The version is supplied by MinVer from the current git tag / height, so no
    version needs to be passed in.  publish\ and dist\ are git-ignored build
    artifacts.

.PARAMETER Configuration
    The MSBuild configuration to build.  Defaults to Release.

.PARAMETER Clean
    Runs `dotnet clean` and removes the publish\ and dist\ output folders before
    building.

.PARAMETER Publish
    After building, produces a self-contained win-x64 publish under publish\.

.PARAMETER Installer
    After building, produces the Inno Setup installer under dist\.  Implies a
    publish step (performed by installer\build-installer.ps1).

.PARAMETER NoRestore
    Skips the implicit restore during build (use when packages are already
    restored).

.EXAMPLE
    .\build.ps1
    Restores and builds the solution in Release.

.EXAMPLE
    .\build.ps1 -Configuration Debug
    Builds the solution in Debug (the configuration used for local validation).

.EXAMPLE
    .\build.ps1 -Clean -Installer
    Cleans, builds, and produces the installer under dist\.
#>
[CmdletBinding()]
param (
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $Clean,
    [switch] $Publish,
    [switch] $Installer,
    [switch] $NoRestore
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $repoRoot 'Tayler.slnx'
$project = Join-Path $repoRoot 'src\TaylerLogTailer\TaylerLogTailer.csproj'
$publishDir = Join-Path $repoRoot 'publish\win-x64'
$distDir = Join-Path $repoRoot 'dist'

if ($Clean) {
    Write-Host "Cleaning ($Configuration)..." -ForegroundColor Cyan
    dotnet clean $solution -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet clean failed with exit code $LASTEXITCODE."
    }

    foreach ($dir in @($publishDir, $distDir)) {
        if (Test-Path $dir) {
            Remove-Item $dir -Recurse -Force
        }
    }
}

Write-Host "Building $solution ($Configuration)..." -ForegroundColor Cyan
$buildArgs = @($solution, '-c', $Configuration, '--nologo')
if ($NoRestore) {
    $buildArgs += '--no-restore'
}

dotnet build @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

if ($Installer) {
    Write-Host "Building installer..." -ForegroundColor Cyan
    & (Join-Path $repoRoot 'installer\build-installer.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Installer build failed with exit code $LASTEXITCODE."
    }
}
elseif ($Publish) {
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

    Write-Host "Publish output is in $publishDir." -ForegroundColor Green
}

Write-Host "Build complete." -ForegroundColor Green
