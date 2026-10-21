<#
.SYNOPSIS
  Builds the Linux Player every role in the nightly Docker Compose smoke test runs (RFC 0001, UM-T17).

.DESCRIPTION
  Builds a throwaway host project (tests/SmokeHost, git-ignored) the way run-sample.ps1 does: PlayModeHost's
  ProjectSettings plus a manifest.json referencing the package by file: path, then imports the Minimal sample through
  Package Manager (MinimalSampleHost.Import). SmokeDriver.cs and SmokeHostBuild.cs are copied in, and
  SmokeHostBuild.BuildFromCommandLine turns off the sample's NetBootstrap "serve when headless" default (the smoke
  test picks server/bot through SmokeDriver's own MINDATTIC_ROLE instead) and builds a StandaloneLinux64 Player into
  -Output (default smoke/build at the repository root, where smoke/Bot.Dockerfile and docker-compose.yml expect it).
  Logs land in tests/SmokeHost/logs/. Exits non-zero on any failure.

.PARAMETER Unity
  Path to the 6000.4.3f1 editor.

.PARAMETER Output
  Where to write the Linux Player build. Defaults to smoke/build at the repository root.
#>
param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.4.3f1\Editor\Unity.exe',
    [string]$Output = ''
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $PSScriptRoot 'SmokeHost'
$logs = Join-Path $project 'logs'
$assets = Join-Path $project 'Assets'
if (-not $Output) { $Output = Join-Path $repo 'smoke/build' }

function Invoke-Unity([string]$log, [string[]]$extra) {
    $a = @('-batchmode', '-nographics', '-projectPath', $project, '-logFile', (Join-Path $logs $log)) + $extra
    $p = Start-Process -FilePath $Unity -ArgumentList $a -Wait -PassThru -NoNewWindow
    return $p.ExitCode
}

# --- the host project ------------------------------------------------------------------------------------------
New-Item -ItemType Directory -Force $logs, (Join-Path $assets 'Editor'), (Join-Path $project 'Packages') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'PlayModeHost/ProjectSettings') $project -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'MinimalSample/Editor/MinimalSampleHost.cs') (Join-Path $assets 'Editor') -Force
Copy-Item (Join-Path $PSScriptRoot 'MinimalSample/Editor/SmokeHostBuild.cs') (Join-Path $assets 'Editor') -Force
@'
{
  "dependencies": {
    "com.mindattic.unity.multiplayer": "file:../../../unity/com.mindattic.unity.multiplayer",
    "com.unity.netcode.gameobjects": "2.11.0",
    "com.unity.transport": "2.7.2",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.physics2d": "1.0.0"
  }
}
'@ | Set-Content -Path (Join-Path $project 'Packages/manifest.json') -Encoding ascii

# --- import the Minimal sample, then add the smoke driver -------------------------------------------------------
Remove-Item (Join-Path $assets 'Samples') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $assets 'Samples.meta') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $logs 'sample-path.txt') -ErrorAction SilentlyContinue
$code = Invoke-Unity 'import.log' @('-executeMethod', 'MinimalSampleHost.Import')
if ($code -ne 0) {
    Write-Host "FAIL: the Minimal sample did not import (Unity exit $code); see $(Join-Path $logs 'import.log')" -ForegroundColor Red
    exit 1
}
Copy-Item (Join-Path $PSScriptRoot 'MinimalSample/Runtime/SmokeDriver.cs') $assets -Force

# --- build -------------------------------------------------------------------------------------------------------
Remove-Item $Output -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $Output | Out-Null
$code = Invoke-Unity 'build.log' @('-executeMethod', 'SmokeHostBuild.BuildFromCommandLine', '-smokeOutput', $Output)
if ($code -ne 0 -or -not (Test-Path (Join-Path $Output 'Game.x86_64'))) {
    Write-Host "FAIL: the smoke build did not produce Game.x86_64 (Unity exit $code); see $(Join-Path $logs 'build.log')" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: built $Output" -ForegroundColor Green
