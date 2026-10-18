<#
.SYNOPSIS
  Decompiles the game's scripting assemblies and URP into decompiled\<Steam build>\ for reading.
.DESCRIPTION
  decompiled\ is gitignored: decompiled code never enters the repo, in any form. Filing output
  by Steam build makes two builds easy to diff after an update. Runs the ilspycmd that
  fetch-tools.ps1 unpacked into .tools, with its update check off so nothing leaves the machine.

  powershell -NoProfile -ExecutionPolicy Bypass -File tools\decompile.ps1 [-GameDir <path>]
#>
param(
    [string]$GameDir = $(if ($env:NUCLEAR_OPTION_GAME_DIR) { $env:NUCLEAR_OPTION_GAME_DIR } else { 'C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option' })
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$managed = Join-Path $GameDir 'NuclearOption_Data\Managed'
$ilspy = Join-Path $repo '.tools\ilspycmd-11.1.0\tools\net10.0\any\ilspycmd.dll'
if (-not (Test-Path $ilspy)) { throw "ilspycmd isn't unpacked. Run tools\fetch-tools.ps1 first." }

$manifest = Join-Path (Split-Path -Parent (Split-Path -Parent $GameDir)) 'appmanifest_2168680.acf'
$build = [regex]::Match((Get-Content $manifest -Raw), '"buildid"\s+"(\d+)"').Groups[1].Value
if (-not $build) { throw "No build ID in $manifest" }
$outRoot = Join-Path $repo "decompiled\$build"

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $repo '.tools\nuget\http-cache'

foreach ($name in 'Assembly-CSharp', 'Assembly-CSharp-firstpass', 'Unity.RenderPipelines.Universal.Runtime', 'Unity.RenderPipelines.Core.Runtime') {
    $out = Join-Path $outRoot $name
    if (Test-Path $out) { "Skipped $name; already in $out"; continue }
    dotnet $ilspy --disable-updatecheck -p --nested-directories -o $out -r $managed (Join-Path $managed "$name.dll") | Out-Null
    if ($LASTEXITCODE) { throw "ilspycmd failed on $name (exit $LASTEXITCODE)." }
    "{0}: {1} files in {2}" -f $name, (Get-ChildItem $out -Recurse -Filter *.cs).Count, $out
}
