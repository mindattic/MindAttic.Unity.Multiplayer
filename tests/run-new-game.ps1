<#
.SYNOPSIS
  Makes a brand-new Unity project outside the repo, runs MindAttic > Multiplayer > New Game in it the way a game developer
  would, and runs the generated game's own EditMode and PlayMode tests (and, with -Smoke, `mp-host game smoke`).

.DESCRIPTION
  In batch mode, each step its own Unity process (package changes and new scripts need a fresh domain):
    1. unity -createProject: a fresh, empty 6000.4.3f1 project (default %TEMP%\MindAttic.NewGame\<GameName>).
    2. Packages/manifest.json gets the multiplayer package by file: path and nothing else (getting-started step 2; the
       package alone must compile and run batch mode, UM-T67), or with -WithTestables also NGO under "testables".
    3. NewGame.CreateFromCommandLine -checkOnly must FAIL (exit 2) and name the missing manifest lines (MindAttic
       Components, the test framework when the project lacks it; Unity.Shared as optional).
    4. ... -addPackages adds them (the kit's repos found beside this one); the manifest must then hold them.
    5. TextMesh Pro's Essential Resources are imported (MindAttic Components does this by itself in an interactive editor).
    6. With -Author: GameTemplateAuthor.Prepare + .Author rebuild the template's scene, prefabs and prefab list and they are
       copied back into Samples~/GameTemplate (then Assets/MyGame is removed again).
    7. NewGame.CreateFromCommandLine -gameName <GameName> creates the game.
    8. The game's EditMode (<GameName>.EditorTests) and PlayMode (<GameName>.Tests) suites run.
    9. A clean checkout (Assets, Packages, ProjectSettings copied, no Library) batch-builds a Win64 player with no compile
       error and leaves ProjectSettings.asset and the asmdefs unchanged (UM-T72); a checkout without MINDATTIC_NGO_TESTABLE
       committed still compiles.
   10. With -Smoke: `mp-host game smoke` (dotnet run of src/Hosting.Cli) builds the Linux player and runs the server and
       two bots under Docker Compose until both pass.
  Logs and results land in <project>\logs. Exits non-zero on any failure. Keep -Root short: step 9's player build fails
  in Burst's link step when the checkout's Temp\Burst paths pass Windows' 260-character limit.

.PARAMETER GameName
  The new game's name (default SpaceCrates, so the template's MyGame token is replaced; -Author forces MyGame).
#>
param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.4.3f1\Editor\Unity.exe',
    [string]$GameName = 'SpaceCrates',
    [string]$Root = (Join-Path $env:TEMP 'MindAttic.NewGame'),
    [switch]$Author,
    [switch]$WithTestables,
    [switch]$Smoke
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$package = Join-Path $repo 'unity/com.mindattic.unity.multiplayer'
$template = Join-Path $package 'Samples~/GameTemplate'
if ($Author) { $GameName = 'MyGame' }
$project = Join-Path $Root $GameName
$logs = Join-Path $Root "$GameName-logs"

function Fail([string]$why) { Write-Host "FAIL: $why" -ForegroundColor Red; exit 1 }

function Invoke-Unity([string]$log, [string[]]$extra) {
    $a = @('-batchmode', '-nographics', '-projectPath', "`"$project`"", '-logFile', "`"$(Join-Path $logs $log)`"") + $extra
    $p = Start-Process -FilePath $Unity -ArgumentList $a -Wait -PassThru -NoNewWindow
    return $p.ExitCode
}

function Invoke-Tests([string]$platform, [string]$assembly) {
    $results = Join-Path $logs "$platform-results.xml"
    Remove-Item $results -ErrorAction SilentlyContinue
    $code = Invoke-Unity "$platform-tests.log" @('-runTests', '-testPlatform', $platform, '-assemblyNames', $assembly, '-testResults', "`"$results`"")
    if (-not (Test-Path $results)) { Fail "no $platform results (Unity exit $code); see $(Join-Path $logs "$platform-tests.log")" }
    $run = ([xml](Get-Content $results -Raw)).'test-run'
    Write-Host "$assembly ($platform): $($run.passed) passed, $($run.failed) failed, $($run.skipped) skipped of $($run.total)"
    if ($code -ne 0 -or [int]$run.failed -gt 0 -or [int]$run.total -eq 0) { Fail "$platform tests (Unity exit $code); see $results" }
}

# --- 1. a fresh project ------------------------------------------------------------------------------------------
if (Test-Path $project) { Remove-Item $project -Recurse -Force }
New-Item -ItemType Directory -Force $logs | Out-Null
Get-ChildItem $logs -File | Remove-Item -Force
$p = Start-Process -FilePath $Unity -ArgumentList @('-batchmode', '-nographics', '-quit', '-createProject', "`"$project`"",
    '-logFile', "`"$(Join-Path $logs 'create.log')`"") -Wait -PassThru -NoNewWindow
if ($p.ExitCode -ne 0 -or -not (Test-Path (Join-Path $project 'Packages/manifest.json'))) { Fail "unity -createProject (exit $($p.ExitCode))" }
Write-Host "Created an empty Unity project at $project"

# --- 2. what a game adds by hand ----------------------------------------------------------------------------------
$manifestPath = Join-Path $project 'Packages/manifest.json'
$manifest = Get-Content $manifestPath -Raw
$line = '"com.mindattic.unity.multiplayer": "file:' + ($package -replace '\\', '/') + '",'
$manifest = $manifest -replace '"dependencies"\s*:\s*\{', ("`"dependencies`": {`n    " + $line)
if (-not $WithTestables) { Write-Host "Added to Packages/manifest.json: $line (no testables)" }
elseif ($manifest -match '"testables"') { $manifest = $manifest -replace '"testables"\s*:\s*\[', "`"testables`": [`n    `"com.unity.netcode.gameobjects`"," }
else { $manifest = $manifest.TrimEnd().TrimEnd('}').TrimEnd() + ",`n  `"testables`": [ `"com.unity.netcode.gameobjects`" ]`n}`n" }
[IO.File]::WriteAllText($manifestPath, $manifest)
if ($WithTestables) { Write-Host "Added to Packages/manifest.json: $line and testables: com.unity.netcode.gameobjects" }

# --- 3. the wizard names what is missing ---------------------------------------------------------------------------
$code = Invoke-Unity 'check.log' @('-executeMethod', 'Unity.Multiplayer.Editor.NewGame.CreateFromCommandLine', '-checkOnly')
$check = Get-Content (Join-Path $logs 'check.log') -Raw
if ($code -ne 2) { Fail "the manifest check should report missing packages (exit 2), got $code; see check.log" }
foreach ($want in '"com.mindattic.components.unity": "file:', 'optional: Unity.Shared') {
    if ($check -notlike "*$want*") { Fail "the manifest check did not name $want; see check.log" }
}
Write-Host 'The wizard named the missing manifest lines (exit 2).'

# --- 4. ... and adds them --------------------------------------------------------------------------------------------
$code = Invoke-Unity 'add.log' @('-executeMethod', 'Unity.Multiplayer.Editor.NewGame.CreateFromCommandLine', '-addPackages')
if ($code -ne 0) { Fail "adding the manifest lines (exit $code); see add.log" }
$manifest = Get-Content $manifestPath -Raw
foreach ($want in 'com.mindattic.components.unity', 'com.mindattic.unity.shared', '"testables"') {
    if ($manifest -notlike "*$want*") { Fail "the manifest still lacks $want" }
}
Write-Host 'The wizard added them:'
Write-Host $manifest

# --- 5. TextMesh Pro's resources (an interactive editor imports them by itself) -------------------------------------
$code = Invoke-Unity 'tmp.log' @('-executeMethod', 'MindAttic.Components.Unity.Editor.TmpEssentials.ImportForBatch')
if ($code -ne 0) { Fail "importing TextMesh Pro's Essential Resources (exit $code); see tmp.log" }

# --- 6. re-author the template's scene and prefabs ------------------------------------------------------------------
if ($Author) {
    $editor = Join-Path $project 'Assets/Editor'
    New-Item -ItemType Directory -Force $editor | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'GameTemplate/Editor/GameTemplateAuthor.cs') $editor -Force
    if ((Invoke-Unity 'prepare.log' @('-executeMethod', 'GameTemplateAuthor.Prepare')) -ne 0) { Fail 'GameTemplateAuthor.Prepare; see prepare.log' }
    if ((Invoke-Unity 'author.log' @('-executeMethod', 'GameTemplateAuthor.Author')) -ne 0) { Fail 'GameTemplateAuthor.Author; see author.log' }
    $authored = Join-Path $project 'Assets/MyGame'
    foreach ($file in 'Game.unity', 'GamePrefabs.asset', 'Resources', 'Resources/Game', 'Resources/Game/Player.prefab', 'Resources/Game/Crate.prefab') {
        $source = Join-Path $authored $file
        $target = Join-Path $template $file
        if (Test-Path $source -PathType Container) { New-Item -ItemType Directory -Force $target | Out-Null }
        else { Copy-Item $source $target -Force }
        Copy-Item "$source.meta" "$target.meta" -Force
    }
    Write-Host "Copied the authored scene, prefabs and prefab list into $template"
    Remove-Item $authored, "$authored.meta", $editor, "$editor.meta" -Recurse -Force
}

# --- 7. the wizard creates the game --------------------------------------------------------------------------------
$code = Invoke-Unity 'create.log' @('-executeMethod', 'Unity.Multiplayer.Editor.NewGame.CreateFromCommandLine', '-gameName', $GameName)
if ($code -ne 0) { Fail "creating the game (exit $code); see create.log" }
foreach ($want in "Assets/$GameName/Game.unity", "Assets/$GameName/Runtime/Player.cs", 'mp-host.json', '.github/workflows/nightly.yml') {
    if (-not (Test-Path (Join-Path $project $want))) { Fail "the wizard did not write $want" }
}
Write-Host "Created $GameName in $project"
Get-Content (Join-Path $project 'mp-host.json')

# --- 8. the game's own tests ----------------------------------------------------------------------------------------
Invoke-Tests 'EditMode' "$GameName.EditorTests"
Invoke-Tests 'PlayMode' "$GameName.Tests"

# --- 9. a clean checkout of the game builds in batch mode (UM-T72) -------------------------------------------------
# What a deploy or CI job sees: the committed files only (Assets, Packages, ProjectSettings, no Library). The first compile
# must not fail (a game test asmdef that references Unity.Multiplayer.Testing without the MINDATTIC_NGO_TESTABLE constraint
# fails it with CS0234 before any editor script runs), and the build must not rewrite ProjectSettings.asset (the define is
# committed, so TestKitDefine has nothing to change).
function Copy-Checkout([string]$to) {
    if (Test-Path $to) { Remove-Item $to -Recurse -Force }
    New-Item -ItemType Directory -Force $to | Out-Null
    foreach ($dir in 'Assets', 'Packages', 'ProjectSettings') { Copy-Item (Join-Path $project $dir) (Join-Path $to $dir) -Recurse }
    Copy-Item (Join-Path $project 'mp-host.json') $to
}
function Get-Tracked([string]$root) {
    $files = @(Get-Item (Join-Path $root 'ProjectSettings/ProjectSettings.asset')) + @(Get-ChildItem (Join-Path $root 'Assets') -Recurse -Filter *.asmdef)
    $map = @{}
    foreach ($f in $files) { $map[$f.FullName.Substring($root.Length)] = (Get-FileHash $f.FullName).Hash }
    return $map
}
function Invoke-Checkout([string]$checkout, [string]$log, [string[]]$extra) {
    $a = @('-batchmode', '-nographics', '-quit', '-projectPath', "`"$checkout`"", '-logFile', "`"$(Join-Path $logs $log)`"") + $extra
    $p = Start-Process -FilePath $Unity -ArgumentList $a -Wait -PassThru -NoNewWindow
    $errors = Select-String -Path (Join-Path $logs $log) -Pattern 'error CS\d+' | Select-Object -First 5
    if ($p.ExitCode -ne 0 -or $errors) { Fail "the clean checkout at $checkout (Unity exit $($p.ExitCode)) $($errors -join ' '); see $log" }
}

$settings = Get-Content (Join-Path $project 'ProjectSettings/ProjectSettings.asset') -Raw
if ($settings -notmatch 'Standalone: [^\r\n]*MINDATTIC_NGO_TESTABLE') { Fail 'ProjectSettings.asset does not hold MINDATTIC_NGO_TESTABLE for Standalone, so a clean checkout would not have it' }

$checkout = Join-Path $Root "$GameName-checkout"
Copy-Checkout $checkout
$before = Get-Tracked $checkout
$player = Join-Path $checkout "Builds/Win64/$GameName.exe"
Invoke-Checkout $checkout 'checkout-build.log' @('-buildTarget', 'Win64', '-buildWindows64Player', "`"$player`"")
if (-not (Test-Path $player)) { Fail "the clean checkout's batch build wrote no player; see checkout-build.log" }
$after = Get-Tracked $checkout
$dirty = @($before.Keys | Where-Object { $after[$_] -ne $before[$_] }) + @($after.Keys | Where-Object { -not $before.ContainsKey($_) })
if ($dirty.Count -gt 0) { Fail "the clean checkout's batch build changed committed files: $($dirty -join ', ')" }
Write-Host "A clean checkout of $GameName built a Win64 player in batch mode and left ProjectSettings.asset and the asmdefs unchanged."

# A game that never committed the define: the first compile still succeeds (its test assemblies are left out until
# TestKitDefine sets the define, then come back on the recompile).
$bare = Join-Path $Root "$GameName-checkout-nodefine"
Copy-Checkout $bare
$path = Join-Path $bare 'ProjectSettings/ProjectSettings.asset'
[IO.File]::WriteAllText($path, ((Get-Content $path -Raw) -replace ';?MINDATTIC_NGO_TESTABLE', ''))
Invoke-Checkout $bare 'checkout-nodefine.log' @()
if ((Get-Content $path -Raw) -notmatch 'MINDATTIC_NGO_TESTABLE') { Fail 'TestKitDefine did not set the define in a checkout that lacked it; see checkout-nodefine.log' }
Write-Host 'A clean checkout without the define committed also compiled (and TestKitDefine set it).'
Remove-Item $checkout, $bare -Recurse -Force

# --- 10. the end-to-end smoke ---------------------------------------------------------------------------------------
if ($Smoke) {
    $cli = Join-Path $repo 'src/Hosting.Cli/Hosting.Cli.csproj'
    & dotnet run --project $cli -c Release -- game smoke --project $project --multiplayer $repo --unity $Unity
    if ($LASTEXITCODE -ne 0) { Fail "mp-host game smoke (exit $LASTEXITCODE); see $(Join-Path $project 'Builds/Smoke/compose.log')" }
}

Write-Host 'PASS' -ForegroundColor Green
