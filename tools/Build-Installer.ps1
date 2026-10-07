# Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
# belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    Builds release\KingdomComeDeliverUs-Setup-<VERSION>.exe and release\send-to-friends\ (+ the zip), after every gate.
.DESCRIPTION
    Gates, in order; the first failure stops the build:
      1. the version is in VERSION (this script never edits it: the user decides what it says);
      2. all tests pass (dotnet test);
      3. the quest catalog files equal what the game's data and the plan produce (Build-QuestCatalog.py --check);
      4. the mod pak builds; its Lua loads without a syntax error (tests do that) and its sha256 is recorded;
      5. tools\perf\soak-record.json says PASS for this version AND for exactly this pak (a changed mod needs a new soak);
      6. the programs publish (self-contained win-x64: a friend needs no .NET);
      7. the installer compiles (Inno Setup 6);
      8. the published relay answers a real client and the agent starts (payload smoke).
    It never runs the installer and never touches the game folder.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Build-Installer.ps1
#>
[CmdletBinding()]
param([switch] $SkipSoakGate)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root
function Step($m) { Write-Host "== $m" -ForegroundColor Cyan }
function Fail($m) { Write-Host "FAILED: $m" -ForegroundColor Red; exit 1 }

Step '1. version'
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { Fail "VERSION '$version' is not Major.Minor.Patch" }
Write-Host "   $version"

Step '2. tests'
dotnet test (Join-Path $root 'dotnet\KcdUs.sln') -nologo -v q | Select-Object -Last 3 | ForEach-Object { Write-Host "   $_" }
if ($LASTEXITCODE -ne 0) { Fail 'tests failed' }

Step '3. quest catalog drift'
python (Join-Path $root 'tools\Build-QuestCatalog.py') --check
if ($LASTEXITCODE -ne 0) { Fail 'the quest catalog files differ from the game data and the plan: run tools\Build-QuestCatalog.py and review' }

Step '4. the mod pak'
$out = python (Join-Path $root 'tools\Build-Pak.py')
if ($LASTEXITCODE -ne 0) { Fail 'the pak did not build' }
Write-Host "   $out"
$pakPath = Join-Path $root 'build\mod\kcdus\Data\kcdus.pak'
$pakHash = (Get-FileHash $pakPath -Algorithm SHA256).Hash.ToLowerInvariant()

Step '4b. what ships: our files only, and the EULA'
$eula = Join-Path $root 'docs\WARHORSE-MODDING-EULA.txt'
if (-not (Test-Path $eula)) { Fail 'docs\WARHORSE-MODDING-EULA.txt is missing: a mod that is passed on must carry Warhorse''s modding EULA (its section 4.7)' }
foreach ($d in 'MENU.md', 'SHARED-WORLDS.md', 'FEATURE-PARITY.md') { if (-not (Test-Path (Join-Path $root "docs\$d"))) { Fail "docs\$d is missing (the installer ships it)" } }
$modFiles = Get-ChildItem (Join-Path $root 'build\mod') -Recurse -File | ForEach-Object { $_.FullName.Substring((Join-Path $root 'build\mod').Length + 1) } | Sort-Object
$expected = @('kcdus\Data\kcdus.pak', 'kcdus\mod.cfg', 'kcdus\mod.manifest') | Sort-Object
if (($modFiles -join '|') -ne ($expected -join '|')) { Fail ("the built mod folder holds more or less than the three files it should: " + ($modFiles -join ', ')) }
Write-Host '   the mod folder is exactly mod.manifest, mod.cfg and Data\kcdus.pak; the Multiplayer tab (kcdus-ui.pak) is built on the player''s computer from the player''s own game files'

Step '5. the frame-rate soak'
$recPath = Join-Path $root 'tools\perf\soak-record.json'
if ($SkipSoakGate) { Write-Host '   SKIPPED by -SkipSoakGate (the installer is not release-grade)' -ForegroundColor Yellow }
else {
    if (-not (Test-Path $recPath)) { Fail 'no soak record: run python tools\perf\soak.py run' }
    $rec = Get-Content $recPath -Raw | ConvertFrom-Json
    if (-not $rec.passed) { Fail 'the soak record says FAIL' }
    if ($rec.version -ne $version) { Fail "the soak was run for $($rec.version), not $version" }
    if ($rec.pak_sha256 -ne $pakHash) { Fail 'the soak was run for a different mod pak: run it again' }
    Write-Host ("   PASS: mean {0:+0.0;-0.0}% p5 {1:+0.0;-0.0}% ({2})" -f $rec.mean_delta_pct, $rec.p5_delta_pct, $rec.date)
}

Step '6. publish (self-contained win-x64)'
& (Join-Path $root 'tools\Build-Engine.ps1')
if($LASTEXITCODE -ne 0){Fail 'engine adapter build failed'}
$rel = Join-Path $root 'release'
$pub = Join-Path $rel 'KCDUS'
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
New-Item -ItemType Directory -Force $pub | Out-Null
foreach ($proj in 'KcdUs.Agent', 'KcdUs.Relay', 'KcdUs.Launcher') {
    dotnet publish (Join-Path $root "dotnet\$proj\$proj.csproj") -c Release -r win-x64 --self-contained true -o $pub -nologo -v q 2>&1 |
        Where-Object { $_ -match 'error' } | ForEach-Object { Write-Host "   $_" }
    if ($LASTEXITCODE -ne 0) { Fail "publish of $proj failed" }
}
foreach ($exe in 'KcdUsAgent.exe', 'KcdUsRelay.exe', 'KcdUsLauncher.exe') { if (-not (Test-Path (Join-Path $pub $exe))) { Fail "$exe is missing from the publish folder" } }
Copy-Item -LiteralPath (Join-Path $root 'build\engine\KcdUsEngineBridge.dll') -Destination $pub
Get-ChildItem $pub -Filter *.pdb | Remove-Item -Force
$foreign = Get-ChildItem $pub -Recurse -File | Where-Object { $_.Extension -in '.pak', '.gfx', '.tbl', '.whs' -or $_.Name -match '^(GameData|Tables|Scripts)' }
if ($foreign) { Fail ("the publish folder holds files that look like the game's own: " + (($foreign | ForEach-Object Name) -join ', ')) }
Write-Host ("   {0:N1} MB in {1} files" -f ((Get-ChildItem $pub -Recurse | Measure-Object Length -Sum).Sum / 1MB), (Get-ChildItem $pub -Recurse -File).Count)

Step '7. installer'
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { Fail 'Inno Setup 6 (ISCC.exe) was not found' }
& $iscc "/DAppVersion=$version" (Join-Path $root 'installer\KCDUS.iss') | Select-Object -Last 4 | ForEach-Object { Write-Host "   $_" }
if ($LASTEXITCODE -ne 0) { Fail 'Inno Setup failed' }
$setup = Join-Path $rel "KingdomComeDeliverUs-Setup-$version.exe"
if (-not (Test-Path $setup)) { Fail 'the installer was not produced' }

Step '8. payload smoke (the published programs, not the build tree)'
$relayExe = Join-Path $pub 'KcdUsRelay.exe'
$port = 17788
$rp = Start-Process $relayExe -ArgumentList '--port', $port -PassThru -WindowStyle Hidden
try {
    Start-Sleep -Seconds 2
    $c = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $port)
    $s = $c.GetStream(); $s.Write([byte[]](0x08, 0, 0), 0, 3)
    # protocol 2: every connection is greeted with a Challenge frame (0x8C) before anything else; a ping ignores it and waits for the Info frame (0x8B)
    $buf = New-Object byte[] 1024; $s.ReadTimeout = 3000
    Start-Sleep -Milliseconds 500
    $n = $s.Read($buf, 0, 1024)
    $off = 0; $found = $false; $text = ''
    while ($off + 3 -le $n) {
        $len = $buf[$off + 1] + 256 * $buf[$off + 2]
        if ($off + 3 + $len -gt $n) { break }
        if ($buf[$off] -eq 0x8B) { $found = $true; $text = [Text.Encoding]::UTF8.GetString($buf, $off + 3, $len); break }
        $off += 3 + $len
    }
    if (-not $found -or $text -notmatch "\|$([regex]::Escape($version))\|") { Fail "the relay's Info answer was wrong: '$text'" }
    Write-Host "   relay answers: $text"
    $c.Close()
} finally { if ($rp -and -not $rp.HasExited) { $rp.Kill() } }
$h = & (Join-Path $pub 'KcdUsAgent.exe') --help
if ($LASTEXITCODE -ne 0 -or ($h -join ' ') -notmatch 'KcdUsAgent') { Fail 'the published agent did not start' }
Write-Host '   agent starts'

Step 'packaging'
$send = Join-Path $rel 'send-to-friends'
if (Test-Path $send) { Remove-Item $send -Recurse -Force }
New-Item -ItemType Directory -Force $send | Out-Null
Copy-Item $setup $send
Copy-Item (Join-Path $root 'docs\READ-ME-FIRST.txt') $send
foreach ($d in 'HUMAN-ACCEPTANCE-TESTS.md', 'CAPABILITIES.md') { Copy-Item (Join-Path $root "docs\$d") $send }
$hash = (Get-FileHash (Join-Path $send (Split-Path $setup -Leaf)) -Algorithm SHA256).Hash
"$hash  $(Split-Path $setup -Leaf)" | Set-Content (Join-Path $send 'SHA256.txt') -Encoding ascii
$zip = Join-Path $rel "KingdomComeDeliverUs-$version-for-friends.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $send '*') -DestinationPath $zip
$zh = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host ("   installer {0:N1} MB  sha256 {1}" -f ((Get-Item $setup).Length / 1MB), $hash)
Write-Host ("   zip       {0:N1} MB  sha256 {1}" -f ((Get-Item $zip).Length / 1MB), $zh)
Write-Host 'BUILD OK' -ForegroundColor Green
