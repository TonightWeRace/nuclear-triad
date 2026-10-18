<#
.SYNOPSIS
  Sends one command to the running plugin and prints its result line.
.DESCRIPTION
  Writes the command to BepInEx\config\nuclear-triad.cmd atomically (temp file, then rename), then
  waits for the plugin to append its result to nuclear-triad.out. The plugin polls once a second,
  and only while the game is updating, so a minimized or paused game holds commands until it resumes.
  Exits 1 when the plugin reports an error.

  powershell -NoProfile -ExecutionPolicy Bypass -File tools\send-command.ps1 dump-cameras menu
#>
param(
    [Parameter(Mandatory = $true, Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$Command,
    [int]$TimeoutSeconds = 15,
    [string]$GameDir = $(if ($env:NUCLEAR_OPTION_GAME_DIR) { $env:NUCLEAR_OPTION_GAME_DIR } else { 'C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option' })
)
$ErrorActionPreference = 'Stop'

$config = Join-Path $GameDir 'BepInEx\config'
$commandPath = Join-Path $config 'nuclear-triad.cmd'
$resultPath = Join-Path $config 'nuclear-triad.out'
$line = ($Command -join ' ').Trim()
if (-not (Get-Process -Name NuclearOption -ErrorAction SilentlyContinue)) { throw 'Nuclear Option is not running.' }

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (Test-Path $commandPath) {
    if ((Get-Date) -gt $deadline) { throw "An earlier command is still waiting in $commandPath." }
    Start-Sleep -Milliseconds 200
}

$before = if (Test-Path $resultPath) { (Get-Item $resultPath).Length } else { 0 }
Set-Content -Path "$commandPath.tmp" -Value $line -Encoding ASCII
Move-Item -Force "$commandPath.tmp" $commandPath

while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 250
    if ((Test-Path $resultPath) -and (Get-Item $resultPath).Length -gt $before) {
        $stream = [IO.File]::Open($resultPath, 'Open', 'Read', 'ReadWrite')
        try {
            [void]$stream.Seek($before, 'Begin')
            $new = (New-Object IO.StreamReader($stream)).ReadToEnd().TrimEnd()
        } finally { $stream.Dispose() }
        $new
        if ($new -match "`terror`t") { exit 1 }
        exit 0
    }
}
throw "No result within $TimeoutSeconds s. Is the plugin loaded, and is the game window updating?"
