<#
.SYNOPSIS
  Installs BepInEx 5 and the dev plugins (UnityExplorer, ConfigurationManager) into the game folder.
.DESCRIPTION
  Uses the verified downloads in .tools\downloads, so run fetch-tools.ps1 first. Refuses to run
  while the game is running, refuses to overwrite any existing file, and refuses an archive with
  an entry that would land outside the game folder. Turns on the BepInEx console and the plugin's
  control channel. Records what it added in .tools\installed.json, which uninstall-mods.ps1 uses
  to take it all back out in one step.

  powershell -NoProfile -ExecutionPolicy Bypass -File tools\install-mods.ps1 [-GameDir <path>]
#>
param(
    [string]$GameDir = $(if ($env:NUCLEAR_OPTION_GAME_DIR) { $env:NUCLEAR_OPTION_GAME_DIR } else { 'C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option' })
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repo = Split-Path -Parent $PSScriptRoot
$downloads = Join-Path $repo '.tools\downloads'
$manifestPath = Join-Path $repo '.tools\installed.json'

if (-not (Test-Path (Join-Path $GameDir 'NuclearOption.exe'))) { throw "No NuclearOption.exe in $GameDir" }
if (Get-Process -Name NuclearOption -ErrorAction SilentlyContinue) { throw 'Nuclear Option is running; close it first.' }
if (Test-Path $manifestPath) { throw "Already installed (see $manifestPath). Run uninstall-mods.ps1 first." }

# Each archive, and the folder its entries land in relative to the game root.
$packages = @(
    @{ Zip = 'bepinex\BepInEx_win_x64_5.4.23.5.zip'; Prefix = '' }
    @{ Zip = 'unityexplorer\UnityExplorer.BepInEx5.Mono.zip'; Prefix = 'BepInEx\' }
    @{ Zip = 'configurationmanager\BepInEx.ConfigurationManager_BepInEx5_v19.0.zip'; Prefix = '' }
)

# Plan every file first, so a conflict stops the install before anything is written. Each
# destination is resolved to a full path, and must be inside the game folder: an entry such as
# ..\..\file, or a name Windows can't use as a path, refuses the whole archive.
$gameRoot = [IO.Path]::GetFullPath($GameDir).TrimEnd('\') + '\'
$plan = foreach ($p in $packages) {
    $zipPath = Join-Path $downloads $p.Zip
    $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName.EndsWith('/')) { continue }
            $rel = $p.Prefix + ($entry.FullName -replace '/', '\')
            try { $dest = [IO.Path]::GetFullPath((Join-Path $GameDir $rel)) } catch { $dest = $null }
            [pscustomobject]@{ Zip = $zipPath; Entry = $entry.FullName; Rel = $rel; Dest = $dest }
        }
    } finally { $zip.Dispose() }
}
$outside = @($plan | Where-Object { -not ($_.Dest -and $_.Dest.StartsWith($gameRoot, [StringComparison]::OrdinalIgnoreCase)) })
if ($outside.Count) {
    throw "Refusing to extract outside the game folder: $(($outside | ForEach-Object { "$($_.Entry) in $(Split-Path -Leaf $_.Zip)" }) -join ', ')"
}
$conflicts = @($plan | Where-Object { Test-Path -LiteralPath $_.Dest })
if ($conflicts.Count) { throw "Refusing to overwrite: $($conflicts.Rel -join ', ')" }

foreach ($group in $plan | Group-Object Zip) {
    $zip = [IO.Compression.ZipFile]::OpenRead($group.Name)
    try {
        foreach ($item in $group.Group) {
            New-Item -ItemType Directory -Force (Split-Path -Parent $item.Dest) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry($item.Entry), $item.Dest)
        }
    } finally { $zip.Dispose() }
}

# BepInEx fills in the rest of its config with defaults on first launch. Nuclear Option destroys
# BepInEx's manager object at startup unless it's hidden, which stops every plugin's Update.
$cfg = Join-Path $GameDir 'BepInEx\config\BepInEx.cfg'
New-Item -ItemType Directory -Force (Split-Path -Parent $cfg) | Out-Null
if (-not (Test-Path $cfg)) {
    Set-Content -Path $cfg -Encoding ASCII -Value @(
        '[Chainloader]', '', 'HideManagerGameObject = true', '',
        '[Logging.Console]', '', '## Enables showing a console for log output.', 'Enabled = true')
}
# The plugin's control channel is off by default, and the dev tools need it. The plugin fills in
# the rest of its config on first launch.
$pluginCfg = Join-Path $GameDir 'BepInEx\config\nuclear-triad.cfg'
if (-not (Test-Path $pluginCfg)) {
    Set-Content -Path $pluginCfg -Encoding ASCII -Value @('[Dev]', '', 'CommandChannel = true')
}

# Everything we add hangs off these root entries; uninstall moves them out whole, which also
# takes the configs, logs and plugins created after this install.
$rootEntries = @($plan | ForEach-Object { ($_.Rel -split '\\')[0] } | Sort-Object -Unique)

# The game's saved screen settings as they are before any span, so uninstall can put them back.
$prefsKey = 'HKCU:\Software\Shockfront\NuclearOption'
$screenPrefs = @()
if (Test-Path $prefsKey) {
    $item = Get-Item $prefsKey
    $screenPrefs = @($item.GetValueNames() | Where-Object { $_ -like 'Screenmanager *' } | ForEach-Object {
        [pscustomobject]@{ Name = $_; Kind = $item.GetValueKind($_).ToString(); Value = $item.GetValue($_) }
    })
}

[pscustomobject]@{
    InstalledAt = (Get-Date).ToString('o')
    GameDir = $GameDir
    RootEntries = $rootEntries
    Files = @($plan.Rel)
    ScreenPrefs = $screenPrefs
} | ConvertTo-Json -Depth 4 | Set-Content -Path $manifestPath -Encoding UTF8

"Installed $($plan.Count) files into $GameDir"
"Root entries added: $($rootEntries -join ', ')"
"Manifest: $manifestPath"
