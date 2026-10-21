<#
.SYNOPSIS
  Imports the package's Minimal sample the way Package Manager does and runs its PlayMode tests.

.DESCRIPTION
  Builds tests/SampleHost (git-ignored): a bare Unity 6000.4.3f1 project with tests/PlayModeHost's ProjectSettings and
  the package by file: path. Then, in three batch-mode runs:
    1. MinimalSampleHost.Import imports "Minimal" through Package Manager's sample API (UnityEditor.PackageManager.UI.Sample)
       into Assets/Samples/MindAttic Multiplayer/<version>/Minimal. A sample missing from package.json, or one that does
       not import, fails here.
    2. With -Author, MinimalSampleHost.Author rebuilds the sample's scene, player prefab and prefab list inside that copy
       (Set Up Scene wizard included) and they are copied back into Samples~/Minimal; the sample is then imported again.
    3. tests/MinimalSample/Tests is copied beside it and its PlayMode suite runs against the imported sample: the
       scene's wiring, hosting from it, and the player between a host and a client.
  Logs and results land in tests/SampleHost/logs/. Exits non-zero on any failure.

.PARAMETER Unity
  Path to the 6000.4.3f1 editor.

.PARAMETER Author
  Regenerate Minimal.unity, MinimalPlayer.prefab and MinimalPrefabs.asset in Samples~/Minimal before testing.
#>
param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.4.3f1\Editor\Unity.exe',
    [switch]$Author
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$sampleSource = Join-Path $repo 'unity/com.mindattic.unity.multiplayer/Samples~/Minimal'
$project = Join-Path $PSScriptRoot 'SampleHost'
$logs = Join-Path $project 'logs'
$assets = Join-Path $project 'Assets'

function Invoke-Unity([string]$log, [string[]]$extra) {
    $a = @('-batchmode', '-nographics', '-projectPath', $project, '-logFile', (Join-Path $logs $log)) + $extra
    $p = Start-Process -FilePath $Unity -ArgumentList $a -Wait -PassThru -NoNewWindow
    return $p.ExitCode
}

function Import-Sample {
    Remove-Item (Join-Path $assets 'Samples') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $assets 'Samples.meta') -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $logs 'sample-path.txt') -ErrorAction SilentlyContinue
    $code = Invoke-Unity 'import.log' @('-executeMethod', 'MinimalSampleHost.Import')
    $pathFile = Join-Path $logs 'sample-path.txt'
    if ($code -ne 0 -or -not (Test-Path $pathFile)) {
        Write-Host "FAIL: the Minimal sample did not import (Unity exit $code); see $(Join-Path $logs 'import.log')" -ForegroundColor Red
        exit 1
    }
    $imported = (Get-Content $pathFile -Raw).Trim()
    Write-Host "Imported the Minimal sample into $imported"
    return $imported
}

# --- the host project ------------------------------------------------------------------------------------------
New-Item -ItemType Directory -Force $logs, (Join-Path $assets 'Editor'), (Join-Path $project 'Packages') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'PlayModeHost/ProjectSettings') $project -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'MinimalSample/Editor/MinimalSampleHost.cs') (Join-Path $assets 'Editor') -Force
Remove-Item (Join-Path $assets 'MinimalSampleTests') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $assets 'MinimalSampleTests.meta') -Force -ErrorAction SilentlyContinue
@'
{
  "dependencies": {
    "com.mindattic.unity.multiplayer": "file:../../../unity/com.mindattic.unity.multiplayer",
    "com.unity.netcode.gameobjects": "2.11.0",
    "com.unity.test-framework": "1.6.0",
    "com.unity.transport": "2.7.2",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.physics2d": "1.0.0"
  },
  "testables": [
    "com.unity.netcode.gameobjects"
  ]
}
'@ | Set-Content -Path (Join-Path $project 'Packages/manifest.json') -Encoding ascii

# --- 1. import (and 2. author) ---------------------------------------------------------------------------------
$imported = Import-Sample
if ($Author) {
    $code = Invoke-Unity 'author.log' @('-executeMethod', 'MinimalSampleHost.Author')
    if ($code -ne 0) {
        Write-Host "FAIL: authoring the sample's assets failed (Unity exit $code); see $(Join-Path $logs 'author.log')" -ForegroundColor Red
        exit 1
    }
    foreach ($name in 'Minimal.unity', 'MinimalPlayer.prefab', 'MinimalPrefabs.asset') {
        foreach ($file in $name, "$name.meta") {
            Copy-Item (Join-Path $project "$imported/$file") (Join-Path $sampleSource $file) -Force
        }
    }
    Write-Host "Copied the authored scene, prefab and prefab list into $sampleSource"
    $imported = Import-Sample
}

# --- 3. the sample's PlayMode tests ----------------------------------------------------------------------------
Copy-Item (Join-Path $PSScriptRoot 'MinimalSample/Tests') (Join-Path $assets 'MinimalSampleTests') -Recurse -Force
$results = Join-Path $logs 'sample-results.xml'
Remove-Item $results -ErrorAction SilentlyContinue
$code = Invoke-Unity 'sample-tests.log' @('-runTests', '-testPlatform', 'PlayMode',
    '-assemblyNames', 'MindAttic.Unity.Multiplayer.Samples.Minimal.Tests', '-testResults', $results)
if (-not (Test-Path $results)) {
    Write-Host "FAIL: no results file (Unity exit $code); see $(Join-Path $logs 'sample-tests.log')" -ForegroundColor Red
    exit 1
}
$run = ([xml](Get-Content $results -Raw)).'test-run'
Write-Host "Minimal sample: $($run.passed) passed, $($run.failed) failed, $($run.skipped) skipped of $($run.total)"
if ($code -ne 0 -or [int]$run.failed -gt 0 -or [int]$run.total -eq 0) {
    Write-Host "FAIL (Unity exit $code); see $results" -ForegroundColor Red
    exit 1
}
Write-Host 'PASS' -ForegroundColor Green
