# SPDX-License-Identifier: GPL-3.0-only
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild=& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if(-not $msbuild){throw 'Visual Studio MSBuild/C++ tools were not found.'}
& $msbuild (Join-Path $taskRoot 'native\EngineRulesTests\EngineRulesTests.vcxproj') /p:Configuration=Release /p:Platform=x64 /verbosity:minimal /nologo
if($LASTEXITCODE -ne 0){throw 'Native stream tests did not compile.'}
& (Join-Path $taskRoot 'build\engine-tests\EngineRulesTests.exe')
if($LASTEXITCODE -ne 0){throw 'Native stream tests failed.'}
