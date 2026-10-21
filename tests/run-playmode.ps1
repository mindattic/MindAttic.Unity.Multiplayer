<#
.SYNOPSIS
  Runs the package's PlayMode suites (Unity.Multiplayer.Tests and the front end's Unity.Multiplayer.UI.Tests) in batch mode.

.DESCRIPTION
  tests/PlayModeHost is a bare Unity 6000.4.3f1 project whose manifest references the package by
  file: path and lists it, and NGO, in "testables". The NetcodeIntegrationTest suites run in one
  editor process over real Unity Transport on loopback (UDP and WebSocket). The host also references
  MindAttic Components and Unity.Shared by file: path (sibling repos), so the optional front end
  (Unity.Multiplayer.UI) compiles and its suite runs too; prepare-host.ps1 imports TextMesh Pro's
  Essential Resources first. Results land in
  tests/PlayModeHost/logs/ (git-ignored). Exits non-zero when any test fails.

.PARAMETER Unity
  Path to the 6000.4.3f1 editor.

.PARAMETER Filter
  Optional test filter (a test or fixture name, or a regex), passed to -testFilter.
#>
param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.4.3f1\Editor\Unity.exe',
    [string]$Filter = ''
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'PlayModeHost'
$logs = Join-Path $project 'logs'
New-Item -ItemType Directory -Force $logs | Out-Null
& (Join-Path $PSScriptRoot 'prepare-host.ps1') -Unity $Unity
if ($LASTEXITCODE -ne 0) { exit 1 }
$results = Join-Path $logs 'playmode-results.xml'
Remove-Item $results -ErrorAction SilentlyContinue

$a = @('-batchmode', '-nographics', '-projectPath', $project, '-logFile', (Join-Path $logs 'playmode.log'),
       '-runTests', '-testPlatform', 'PlayMode', '-assemblyNames', 'Unity.Multiplayer.Tests;Unity.Multiplayer.UI.Tests',
       '-testResults', $results)
if ($Filter) { $a += @('-testFilter', $Filter) }
$p = Start-Process -FilePath $Unity -ArgumentList $a -Wait -PassThru -NoNewWindow

if (-not (Test-Path $results)) {
    Write-Host "FAIL: no results file (Unity exit $($p.ExitCode)); see $(Join-Path $logs 'playmode.log')" -ForegroundColor Red
    exit 1
}
$run = ([xml](Get-Content $results -Raw)).'test-run'
Write-Host "PlayMode: $($run.passed) passed, $($run.failed) failed, $($run.skipped) skipped of $($run.total)"
if ($p.ExitCode -ne 0 -or [int]$run.failed -gt 0 -or [int]$run.total -eq 0) {
    Write-Host "FAIL (Unity exit $($p.ExitCode)); see $results" -ForegroundColor Red
    exit 1
}
Write-Host 'PASS' -ForegroundColor Green
