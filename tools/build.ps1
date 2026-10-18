<#
.SYNOPSIS
  Builds Nuclear Triad, runs the tests, and deploys the plugin into the game's BepInEx folder.
.DESCRIPTION
  Keeps NuGet's packages and caches inside .tools\nuget, so the project stays self-contained, and
  opts this process out of .NET CLI telemetry. Deploying needs the game closed, because Windows
  locks loaded plugin DLLs.

  powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 [-NoDeploy] [-NoTest] [-Configuration Debug]
#>
param(
    [switch]$NoDeploy,
    [switch]$NoTest,
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repo 'NuclearTriad.sln'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $repo '.tools\nuget\http-cache'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $repo '.tools\nuget\plugins-cache'

$deploy = -not $NoDeploy
if ($deploy -and (Get-Process -Name NuclearOption -ErrorAction SilentlyContinue)) {
    throw 'Nuclear Option is running. Close it before deploying, or pass -NoDeploy.'
}

dotnet build $solution -c $Configuration --disable-build-servers "-p:Deploy=$deploy"
if ($LASTEXITCODE) { throw "Build failed (exit $LASTEXITCODE)." }

if (-not $NoTest -and (Test-Path (Join-Path $repo 'tests'))) {
    dotnet test $solution -c $Configuration --no-build
    if ($LASTEXITCODE) { throw "Tests failed (exit $LASTEXITCODE)." }
}
