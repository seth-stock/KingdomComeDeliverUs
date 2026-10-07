# Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
<#
.SYNOPSIS
    Reports whether every layer of a build or an install says the same version, and prints the payload hashes a bug report needs.

.DESCRIPTION
    VERSION is the one source (this script never edits it). The .NET programs stamp it into their file and product versions. The Lua pak and the engine bridge carry no version:
    they are identified by SHA-256, which is also what the room handshake enforces (the pak) or reports (the bridge, the engine).
    Exit 0 when every program agrees with VERSION; 1 otherwise.

.PARAMETER Install
    Also check the installed copy (%LocalAppData%\KCDUS and the game's Mods\kcdus).

.PARAMETER GameDir
    The game folder, to hash the installed pak and the engine DLL.
#>
param([switch]$Install, [string]$GameDir)

$root = Split-Path $PSScriptRoot -Parent
$version = (Get-Content (Join-Path $root 'VERSION') -TotalCount 1).Trim()
$bad = 0
function Check($label, $path) {
    if (-not (Test-Path $path)) { Write-Host ("  MISSING {0}  ({1})" -f $label, $path) -ForegroundColor Yellow; return }
    $v = (Get-Item $path).VersionInfo.ProductVersion
    $ok = $v -eq $version
    if (-not $ok) { $script:bad++ }
    Write-Host ("  {0} {1,-28} {2}" -f $(if ($ok) { 'ok     ' } else { 'DIFFERS' }), $label, $v) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}
function Hash($label, $path) {
    if (Test-Path $path) { Write-Host ("  sha256  {0,-28} {1}" -f $label, (Get-FileHash $path -Algorithm SHA256).Hash.ToLower()) }
    else { Write-Host ("  MISSING {0}" -f $label) -ForegroundColor Yellow }
}

Write-Host "VERSION file: $version"
$targets = @(@{ Name = 'release payload'; Dir = (Join-Path $root 'release\KCDUS') })
if ($Install) { $targets += @{ Name = 'installed'; Dir = (Join-Path $env:LOCALAPPDATA 'KCDUS') } }
foreach ($t in $targets) {
    Write-Host "== $($t.Name): $($t.Dir)"
    foreach ($exe in 'KcdUsAgent.exe', 'KcdUsRelay.exe', 'KcdUsLauncher.exe') { Check $exe (Join-Path $t.Dir $exe) }
    Hash 'KcdUsEngineBridge.dll' (Join-Path $t.Dir 'KcdUsEngineBridge.dll')
}
Write-Host '== the mod (Lua pak)'
Hash 'built kcdus.pak' (Join-Path $root 'build\mod\kcdus\Data\kcdus.pak')
if ($Install -and $GameDir) {
    Hash 'installed kcdus.pak' (Join-Path $GameDir 'Mods\kcdus\Data\kcdus.pak')
    Hash 'game WHGame.dll (engine)' (Join-Path $GameDir 'Bin\Win64\WHGame.dll')
}
Hash "Setup-$version.exe" (Join-Path $root "release\KingdomComeDeliverUs-Setup-$version.exe")
if ($bad -gt 0) { Write-Host "$bad program(s) disagree with VERSION" -ForegroundColor Red; exit 1 }
Write-Host 'every program agrees with VERSION' -ForegroundColor Green
exit 0
