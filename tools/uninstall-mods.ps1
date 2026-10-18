<#
.SYNOPSIS
  Returns the game folder to vanilla in one step.
.DESCRIPTION
  Moves the root entries install-mods.ps1 recorded (the doorstop files and the whole BepInEx
  folder, including configs, logs and plugins added since) into .tools\uninstalled\<timestamp>\.
  Nothing is deleted, and files that came with the game are never touched. Refuses to run while
  the game is running.

  powershell -NoProfile -ExecutionPolicy Bypass -File tools\uninstall-mods.ps1
#>
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repo '.tools\installed.json'
if (-not (Test-Path $manifestPath)) { throw "No install manifest at $manifestPath; nothing to uninstall." }
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
if (Get-Process -Name NuclearOption -ErrorAction SilentlyContinue) { throw 'Nuclear Option is running; close it first.' }

$dest = Join-Path $repo (".tools\uninstalled\" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $dest | Out-Null
foreach ($name in $manifest.RootEntries) {
    $path = Join-Path $manifest.GameDir $name
    if (Test-Path -LiteralPath $path) {
        Move-Item -LiteralPath $path -Destination (Join-Path $dest $name)
        "Moved $name"
    }
}
# A spanned window can end up as the game's saved screen setting; put back the values recorded
# at install time.
$prefsKey = 'HKCU:\Software\Shockfront\NuclearOption'
if ($manifest.ScreenPrefs -and (Test-Path $prefsKey)) {
    foreach ($pref in $manifest.ScreenPrefs) {
        Set-ItemProperty -Path $prefsKey -Name $pref.Name -Value $pref.Value -Type $pref.Kind
    }
    "Restored $(@($manifest.ScreenPrefs).Count) saved screen settings"
}

Move-Item -LiteralPath $manifestPath -Destination (Join-Path $dest 'installed.json')
"The game folder is back to vanilla. What was removed is in $dest"
