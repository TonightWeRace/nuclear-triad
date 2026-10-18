<#
.SYNOPSIS
  Builds the release zip: the plugin laid out the way it goes into the game folder, with the
  install guide.
.DESCRIPTION
  Refuses while the plugin's sources or the guide have uncommitted changes, so a zip is always what
  a commit builds. Builds and tests the plugin without deploying it. Then it writes three files to
  .tools\release\:
  - NuclearTriad-<version>.zip, holding BepInEx/plugins/NuclearTriad/ with NuclearTriad.dll,
    README.md and LICENSE;
  - its SHA-256;
  - release notes for GitHub.
  README.md is the install guide, docs\install.md, and LICENSE is the repo's. BepInEx and
  ConfigurationManager are prerequisites the guide installs, so neither goes in the zip. This
  script publishes nothing.

  powershell -NoProfile -ExecutionPolicy Bypass -File tools\package.ps1 [-AllowDirty]

  -AllowDirty builds from uncommitted changes, for a trial; the zip's commit then ends in -dirty.
#>
param([switch]$AllowDirty)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$repo = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repo '.tools\release'
$guide = Join-Path $repo 'docs\install.md'
$license = Join-Path $repo 'LICENSE'
$publicRepo = 'TonightWeRace/nuclear-triad'

$inputs = 'src', 'tests', 'docs/install.md', 'LICENSE', 'Directory.Build.props', 'NuclearTriad.sln', 'tools/build.ps1', 'tools/package.ps1'
$dirty = @(git -C $repo status --porcelain -- $inputs)
if ($LASTEXITCODE) { throw 'git status failed.' }
if ($dirty.Count -and -not $AllowDirty) { throw "Uncommitted changes to what the zip is built from:`n$($dirty -join "`n")`nCommit them first, or pass -AllowDirty for a trial." }
$commit = (git -C $repo rev-parse --short HEAD).Trim() + $(if ($dirty.Count) { '-dirty' })

$version = [regex]::Match([IO.File]::ReadAllText((Join-Path $repo 'src\NuclearTriad\Plugin.cs')), 'const string Version = "([^"]+)"').Groups[1].Value
$steamBuild = [regex]::Match([IO.File]::ReadAllText((Join-Path $repo 'src\NuclearTriad\Dev\VersionGuard.cs')), 'VerifiedSteamBuild = "(\d+)"').Groups[1].Value
if (-not $version -or -not $steamBuild) { throw 'Couldn''t read the version from Plugin.cs or the verified build from VersionGuard.cs.' }
$zipName = "NuclearTriad-$version.zip"

# The guide states the version, the zip's name and the game build it was tested on; a stale one
# would send people to the wrong download.
$guideText = [IO.File]::ReadAllText($guide)
foreach ($needed in "**Version $version**", $zipName, "Steam build $steamBuild", "Loading [Nuclear Triad $version]") {
    if (-not $guideText.Contains($needed)) { throw "docs\install.md doesn't say '$needed'. Bring it up to date for $version." }
}
$gameVersion = [regex]::Match($guideText, 'tested on Nuclear Option (\S+) \(Steam build').Groups[1].Value

& (Join-Path $PSScriptRoot 'build.ps1') -NoDeploy
$dll = Join-Path $repo 'src\NuclearTriad\bin\Release\net48\NuclearTriad.dll'

# Each entry in the zip, at the path it takes in the game folder, and where it comes from.
$entries = [ordered]@{
    'BepInEx/plugins/NuclearTriad/NuclearTriad.dll' = $dll
    'BepInEx/plugins/NuclearTriad/README.md' = $guide
    'BepInEx/plugins/NuclearTriad/LICENSE' = $license
}

New-Item -ItemType Directory -Force -Path $out | Out-Null
$zipPath = Join-Path $out $zipName
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath }
$zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    foreach ($name in $entries.Keys) { [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $entries[$name], $name, 'Optimal') }
} finally { $zip.Dispose() }

# Read it back: exactly these entries, each byte for byte its source.
function Get-Sha256Stream([IO.Stream]$stream) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '').ToLowerInvariant() } finally { $sha.Dispose() }
}
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $names = @($zip.Entries | ForEach-Object { $_.FullName })
    if (($names -join '|') -ne (@($entries.Keys) -join '|')) { throw "The zip holds $($names -join ', '), not just $(@($entries.Keys) -join ', ')." }
    $listing = foreach ($entry in $zip.Entries) {
        $s = $entry.Open()
        try { $hash = Get-Sha256Stream $s } finally { $s.Dispose() }
        if ($hash -ne (Get-FileHash -LiteralPath $entries[$entry.FullName] -Algorithm SHA256).Hash.ToLowerInvariant()) { throw "$($entry.FullName) in the zip isn't its source." }
        '  {0,-48} {1,8} bytes  {2}' -f $entry.FullName, $entry.Length, $hash
    }
} finally { $zip.Dispose() }

# A path on this machine inside the DLL would go to everyone who downloads it. Directory.Build.props
# maps them all to /_/; this catches a build that doesn't.
$bytes = [IO.File]::ReadAllBytes($dll)
foreach ($encoding in [Text.Encoding]::ASCII, [Text.Encoding]::Unicode) {
    $found = [regex]::Match($encoding.GetString($bytes), '[A-Za-z]:\\[\x20-\x7e]{3,}')
    if ($found.Success) { throw "NuclearTriad.dll holds a local path ($($found.Value)); fix the build before releasing." }
}

$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$zipPath.sha256", "$zipHash  $zipName`r`n")

$notesPath = Join-Path $out "release-notes-$version.md"
[IO.File]::WriteAllText($notesPath, @"
Nuclear Triad $version for Nuclear Option: one correctly projected view per screen on a triple-screen rig.

**Install:** extract ``$zipName`` into the game folder, the one with ``NuclearOption.exe``. It adds ``BepInEx\plugins\NuclearTriad\``. It needs BepInEx 5.4.23.5 and ConfigurationManager 19.0. The [install guide](https://github.com/$publicRepo/blob/main/docs/install.md), also in the zip as README.md, walks through installing them and setting up a rig.

Tested on Nuclear Option $gameVersion (Steam build $steamBuild) with Direct3D 11.

SHA-256 of ``$zipName``: ``$zipHash``
"@, (New-Object Text.UTF8Encoding $false))

$gameDir = if ($env:NUCLEAR_OPTION_GAME_DIR) { $env:NUCLEAR_OPTION_GAME_DIR } else { 'C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option' }
$deployed = Join-Path $gameDir 'BepInEx\plugins\NuclearTriad\NuclearTriad.dll'
$devMachine = if (-not (Test-Path -LiteralPath $deployed)) { 'has no deployed plugin' }
    elseif ((Get-FileHash -LiteralPath $deployed -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash) { 'runs this exact plugin build' }
    else { 'runs a different plugin build' }

''
"Nuclear Triad $version, commit $commit"
"$zipPath ($((Get-Item -LiteralPath $zipPath).Length) bytes)"
"  SHA-256 $zipHash"
$listing
"Release notes: $notesPath"
"This machine $devMachine."
