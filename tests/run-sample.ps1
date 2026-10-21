<#
.SYNOPSIS
  Imports one of the package's samples the way Package Manager does and runs its PlayMode tests.

.DESCRIPTION
  Builds tests/SampleHost (git-ignored): a bare Unity 6000.4.3f1 project with tests/PlayModeHost's ProjectSettings and
  the package by file: path. Then, in three batch-mode runs:
    1. MinimalSampleHost.Import imports the sample through Package Manager's sample API (UnityEditor.PackageManager.UI.Sample)
       into Assets/Samples/MindAttic Multiplayer/<version>/<sample>. A sample missing from package.json, or one that does
       not import, fails here.
    2. With -Author, the sample's authoring method (MinimalSampleHost.Author, FrontEndSampleHost.Author) rebuilds its scene,
       player prefab and prefab list inside that copy (Set Up Scene / Add Front End included) and they are copied back into
       Samples~/<sample>; the sample is then imported again.
    3. The sample's tests (tests/MinimalSample/Tests, tests/FrontEndSample/Tests) are copied beside it and their PlayMode
       suite runs against the imported sample.
  The Front End sample also needs MindAttic Components and Unity.Shared, which the host references by file: path (sibling
  repos), and TextMesh Pro's Essential Resources, imported in a pass of their own first.
  Logs and results land in tests/SampleHost/logs/. Exits non-zero on any failure.

.PARAMETER Unity
  Path to the 6000.4.3f1 editor.

.PARAMETER Sample
  Minimal (default) or FrontEnd.

.PARAMETER Author
  Regenerate the sample's scene, player prefab and prefab list in Samples~/<sample> before testing.
#>
param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.4.3f1\Editor\Unity.exe',
    [ValidateSet('Minimal', 'FrontEnd')] [string]$Sample = 'Minimal',
    [switch]$Author
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $PSScriptRoot 'SampleHost'
$logs = Join-Path $project 'logs'
$assets = Join-Path $project 'Assets'

# Per sample: its display name in package.json, its folder under Samples~, the authored files, the authoring method,
# the tests' folder and assembly.
$samples = @{
    Minimal  = @{ Name = 'Minimal'; Folder = 'Minimal'; Files = @('Minimal.unity', 'MinimalPlayer.prefab', 'MinimalPrefabs.asset');
                  Author = 'MinimalSampleHost.Author'; Tests = 'MinimalSample'; Assembly = 'Unity.Multiplayer.Samples.Minimal.Tests' }
    FrontEnd = @{ Name = 'Front End'; Folder = 'FrontEnd'; Files = @('FrontEnd.unity', 'FrontEndPlayer.prefab', 'FrontEndPrefabs.asset');
                  Author = 'FrontEndSampleHost.Author'; Tests = 'FrontEndSample'; Assembly = 'Unity.Multiplayer.Samples.FrontEnd.Tests' }
}
$s = $samples[$Sample]
$sampleSource = Join-Path $repo "unity/com.mindattic.unity.multiplayer/Samples~/$($s.Folder)"

function Invoke-Unity([string]$log, [string[]]$extra) {
    $a = @('-batchmode', '-nographics', '-projectPath', $project, '-logFile', (Join-Path $logs $log)) + $extra
    $p = Start-Process -FilePath $Unity -ArgumentList $a -Wait -PassThru -NoNewWindow
    return $p.ExitCode
}

function Import-Sample {
    Remove-Item (Join-Path $assets 'Samples') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $assets 'Samples.meta') -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $logs 'sample-path.txt') -ErrorAction SilentlyContinue
    $code = Invoke-Unity 'import.log' @('-executeMethod', 'MinimalSampleHost.Import', '-sampleName', "`"$($s.Name)`"")
    $pathFile = Join-Path $logs 'sample-path.txt'
    if ($code -ne 0 -or -not (Test-Path $pathFile)) {
        Write-Host "FAIL: the $($s.Name) sample did not import (Unity exit $code); see $(Join-Path $logs 'import.log')" -ForegroundColor Red
        exit 1
    }
    $imported = (Get-Content $pathFile -Raw).Trim()
    Write-Host "Imported the $($s.Name) sample into $imported"
    return $imported
}

# --- the host project ------------------------------------------------------------------------------------------
New-Item -ItemType Directory -Force $logs, (Join-Path $assets 'Editor'), (Join-Path $project 'Packages') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'PlayModeHost/ProjectSettings') $project -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'MinimalSample/Editor/MinimalSampleHost.cs') (Join-Path $assets 'Editor') -Force
Copy-Item (Join-Path $PSScriptRoot 'FrontEndSample/Editor/FrontEndSampleHost.cs') (Join-Path $assets 'Editor') -Force
foreach ($old in 'MinimalSampleTests', 'FrontEndSampleTests') {
    Remove-Item (Join-Path $assets $old) -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $assets "$old.meta") -Force -ErrorAction SilentlyContinue
}
# The Front End sample's two extra packages, as a game adds them (sibling repos, see docs/articles/front-end.md).
$frontEndPackages = if ($Sample -eq 'FrontEnd') { @'
    "com.mindattic.components.unity": "file:../../../../MindAttic.Components/unity/com.mindattic.components.unity",
    "com.mindattic.unity.shared": "file:../../../../Unity.Shared/unity/com.mindattic.unity.shared",
'@ } else { '' }
@"
{
  "dependencies": {
$frontEndPackages
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
"@ | Set-Content -Path (Join-Path $project 'Packages/manifest.json') -Encoding ascii

if ($Sample -eq 'FrontEnd' -and -not (Test-Path (Join-Path $assets 'TextMesh Pro\Resources\TMP Settings.asset'))) {
    $code = Invoke-Unity 'prepare.log' @('-executeMethod', 'MindAttic.Components.Unity.Editor.TmpEssentials.ImportForBatch')
    if ($code -ne 0) {
        Write-Host "FAIL: importing TextMesh Pro's Essential Resources (Unity exit $code); see $(Join-Path $logs 'prepare.log')" -ForegroundColor Red
        exit 1
    }
}

# --- 1. import (and 2. author) ---------------------------------------------------------------------------------
$imported = Import-Sample
if ($Author) {
    $code = Invoke-Unity 'author.log' @('-executeMethod', $s.Author)
    if ($code -ne 0) {
        Write-Host "FAIL: authoring the sample's assets failed (Unity exit $code); see $(Join-Path $logs 'author.log')" -ForegroundColor Red
        exit 1
    }
    foreach ($name in $s.Files) {
        foreach ($file in $name, "$name.meta") {
            Copy-Item (Join-Path $project "$imported/$file") (Join-Path $sampleSource $file) -Force
        }
    }
    Write-Host "Copied the authored scene, prefab and prefab list into $sampleSource"
    $imported = Import-Sample
}

# --- 3. the sample's PlayMode tests ----------------------------------------------------------------------------
Copy-Item (Join-Path $PSScriptRoot "$($s.Tests)/Tests") (Join-Path $assets "$($s.Tests)Tests") -Recurse -Force
$results = Join-Path $logs 'sample-results.xml'
Remove-Item $results -ErrorAction SilentlyContinue
$code = Invoke-Unity 'sample-tests.log' @('-runTests', '-testPlatform', 'PlayMode',
    '-assemblyNames', $s.Assembly, '-testResults', $results)
if (-not (Test-Path $results)) {
    Write-Host "FAIL: no results file (Unity exit $code); see $(Join-Path $logs 'sample-tests.log')" -ForegroundColor Red
    exit 1
}
$run = ([xml](Get-Content $results -Raw)).'test-run'
Write-Host "$($s.Name) sample: $($run.passed) passed, $($run.failed) failed, $($run.skipped) skipped of $($run.total)"
if ($code -ne 0 -or [int]$run.failed -gt 0 -or [int]$run.total -eq 0) {
    Write-Host "FAIL (Unity exit $code); see $results" -ForegroundColor Red
    exit 1
}
Write-Host 'PASS' -ForegroundColor Green
