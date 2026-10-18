<#
.SYNOPSIS
  Downloads the third-party tools the dev loop uses into .tools\downloads and checks each
  against a pinned checksum.
.DESCRIPTION
  Each file lands in its own folder under .tools\downloads. A file whose size or hash doesn't
  match is renamed to *.bad and the script stops. Re-running skips files already present and
  verified. These versions are pinned on purpose; changing one means checking the new release
  and a new pin.

  powershell -NoProfile -ExecutionPolicy Bypass -File tools\fetch-tools.ps1
#>
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$repo = Split-Path -Parent $PSScriptRoot
$downloads = Join-Path $repo '.tools\downloads'

# Hashes are the publishers' own: GitHub's release-asset digests and nuget.org's package hash.
$tools = @(
    @{ Name = 'bepinex'; File = 'BepInEx_win_x64_5.4.23.5.zip'; Size = 639118; Algorithm = 'SHA256'
       Hash = '82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4'
       Url = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip' }
    @{ Name = 'unityexplorer'; File = 'UnityExplorer.BepInEx5.Mono.zip'; Size = 3467709; Algorithm = 'SHA256'
       Hash = '42d30d01ec01e3e9a36fa6e925849453c8f30783bc8d95322c4e25e3a3e2767a'
       Url = 'https://github.com/yukieiji/UnityExplorer/releases/download/v4.13.6/UnityExplorer.BepInEx5.Mono.zip' }
    @{ Name = 'configurationmanager'; File = 'BepInEx.ConfigurationManager_BepInEx5_v19.0.zip'; Size = 84258; Algorithm = 'SHA256'
       Hash = 'eed83f6e6accbdd0c3771f3061a6ae05b979b9b9b8a00dead69aabc1bf5fe950'
       Url = 'https://github.com/BepInEx/BepInEx.ConfigurationManager/releases/download/v19.0/BepInEx.ConfigurationManager_BepInEx5_v19.0.zip' }
    @{ Name = 'ilspycmd'; File = 'ilspycmd.11.1.0.9782.nupkg'; Size = 4632113; Algorithm = 'SHA512'
       Hash = '17b0a5bbfad858fb7d7c29432f3660597234376fd1b78e2de6d3ae90e61466dd6d23c930653098f3311956022bede4e425086e645256e9127c1f34429709c842'
       Url = 'https://api.nuget.org/v3-flatcontainer/ilspycmd/11.1.0.9782/ilspycmd.11.1.0.9782.nupkg' }
    @{ Name = 'gh'; File = 'gh_2.102.0_windows_amd64.zip'; Size = 15512013; Algorithm = 'SHA256'
       Hash = 'ae64e556ecc240b200f7eba60d550e4bb60d78e860e69dd88c449405b86067f4'
       Url = 'https://github.com/cli/cli/releases/download/v2.102.0/gh_2.102.0_windows_amd64.zip' }
)

foreach ($t in $tools) {
    $dir = Join-Path $downloads $t.Name
    $path = Join-Path $dir $t.File
    New-Item -ItemType Directory -Force $dir | Out-Null
    if (-not (Test-Path $path)) {
        "Downloading $($t.File)"
        Invoke-WebRequest -Uri $t.Url -OutFile $path -UseBasicParsing
    }
    $size = (Get-Item $path).Length
    $hash = (Get-FileHash -Algorithm $t.Algorithm $path).Hash.ToLowerInvariant()
    if ($size -ne $t.Size -or $hash -ne $t.Hash) {
        Move-Item -Force $path "$path.bad"
        throw "$($t.File): expected $($t.Size) bytes and $($t.Algorithm) $($t.Hash), got $size bytes and $hash. Kept as $path.bad."
    }
    "OK  {0,-50} {1,9} bytes  {2} verified" -f $t.File, $size, $t.Algorithm
}

# Unpack the tools that run from .tools rather than being installed machine-wide. ilspycmd is a
# .NET tool package that decompile.ps1 runs on the installed .NET 10 runtime; gh is portable.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$unpack = @(
    @{ Archive = 'ilspycmd\ilspycmd.11.1.0.9782.nupkg'; Dir = 'ilspycmd-11.1.0' }
    @{ Archive = 'gh\gh_2.102.0_windows_amd64.zip'; Dir = 'gh-2.102.0' }
)
foreach ($u in $unpack) {
    $dir = Join-Path $repo ".tools\$($u.Dir)"
    if (-not (Test-Path $dir)) {
        [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $downloads $u.Archive), $dir)
        "Unpacked $($u.Dir) into $dir"
    }
}
