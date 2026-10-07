# SPDX-License-Identifier: GPL-3.0-only
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if(-not (Test-Path -LiteralPath $vswhere)){throw 'Install Visual Studio C++ build tools (v143) and the Windows SDK.'}
$msbuild=& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if(-not $msbuild){throw 'MSBuild was not found.'}
& $msbuild (Join-Path $taskRoot 'native\KcdUs.EngineBridge\KcdUs.EngineBridge.vcxproj') /p:Configuration=Release /p:Platform=x64 /verbosity:minimal /nologo
if($LASTEXITCODE -ne 0){throw 'Engine adapter build failed.'}
