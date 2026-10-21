<#
.SYNOPSIS
  Readies tests/PlayModeHost before a test run: imports TextMesh Pro's Essential Resources once.

.DESCRIPTION
  The front end (Unity.Multiplayer.UI, UM-T48) is built from MindAttic Components, which draws text with TextMesh Pro.
  TMP needs its Essential Resources in the project; Components' editor imports them on first load, but doing that in
  the middle of a test run would reimport assets under the tests. On a clean checkout this runs that import in its own
  batch-mode pass first (the resources are git-ignored in the host). run-playmode.ps1 and run-editmode.ps1 call it.
  Exits non-zero when the import fails.

.PARAMETER Unity
  Path to the 6000.4.3f1 editor.
#>
param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.4.3f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'PlayModeHost'
$logs = Join-Path $project 'logs'
New-Item -ItemType Directory -Force $logs | Out-Null
if (Test-Path (Join-Path $project 'Assets\TextMesh Pro\Resources\TMP Settings.asset')) { exit 0 }

$p = Start-Process -FilePath $Unity -Wait -PassThru -NoNewWindow -ArgumentList @(
    '-batchmode', '-nographics', '-projectPath', $project, '-logFile', (Join-Path $logs 'prepare.log'),
    '-executeMethod', 'MindAttic.Components.Unity.Editor.TmpEssentials.ImportForBatch')
if ($p.ExitCode -ne 0) {
    Write-Host "FAIL: preparing the host project (Unity exit $($p.ExitCode)); see $(Join-Path $logs 'prepare.log')" -ForegroundColor Red
    exit 1
}
