<#
.SYNOPSIS
  Runs the package's EditMode suite (Unity.Multiplayer.Editor.Tests) in batch mode.

.DESCRIPTION
  Uses the same bare Unity 6000.4.3f1 host project as run-playmode.ps1 (tests/PlayModeHost, the package listed in
  "testables"). Covers the editor tooling: the Set Up Scene wizard, the Add Front End step, the prefab validator and
  the Build Dedicated Server (Linux) menu, including one real headless Linux build (Mono) with its Dockerfile, which
  needs the Linux Dedicated Server build support module (the test is ignored without it). Results land in tests/PlayModeHost/logs/
  (git-ignored). Exits non-zero when any test fails.

.PARAMETER Unity
  Path to the 6000.4.3f1 editor.

.PARAMETER Filter
  Optional test filter (a test or fixture name, or a regex), passed to -testFilter.

.PARAMETER SkipBuild
  Leave out the [Category("Build")] test that makes a real Linux server build.
#>
param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.4.3f1\Editor\Unity.exe',
    [string]$Filter = '',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'PlayModeHost'
$logs = Join-Path $project 'logs'
New-Item -ItemType Directory -Force $logs | Out-Null
& (Join-Path $PSScriptRoot 'prepare-host.ps1') -Unity $Unity
if ($LASTEXITCODE -ne 0) { exit 1 }
$results = Join-Path $logs 'editmode-results.xml'
Remove-Item $results -ErrorAction SilentlyContinue

$a = @('-batchmode', '-nographics', '-projectPath', $project, '-logFile', (Join-Path $logs 'editmode.log'),
       '-runTests', '-testPlatform', 'EditMode', '-assemblyNames', 'Unity.Multiplayer.Editor.Tests',
       '-testResults', $results)
if ($Filter) { $a += @('-testFilter', $Filter) }
if ($SkipBuild) { $a += @('-testCategory', '!Build') }
$p = Start-Process -FilePath $Unity -ArgumentList $a -Wait -PassThru -NoNewWindow

if (-not (Test-Path $results)) {
    Write-Host "FAIL: no results file (Unity exit $($p.ExitCode)); see $(Join-Path $logs 'editmode.log')" -ForegroundColor Red
    exit 1
}
$run = ([xml](Get-Content $results -Raw)).'test-run'
Write-Host "EditMode: $($run.passed) passed, $($run.failed) failed, $($run.skipped) skipped of $($run.total)"
if ($p.ExitCode -ne 0 -or [int]$run.failed -gt 0 -or [int]$run.total -eq 0) {
    Write-Host "FAIL (Unity exit $($p.ExitCode)); see $results" -ForegroundColor Red
    exit 1
}
Write-Host 'PASS' -ForegroundColor Green
