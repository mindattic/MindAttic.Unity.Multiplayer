<#
.SYNOPSIS
  Re-runs the UM-T4 generator spike end to end (see docs/rfc/0002-generator-spike.md).

.DESCRIPTION
  1. dotnet build the toy generator (netstandard2.0, Microsoft.CodeAnalysis.CSharp 4.3.1) and copy
     it to Unity/Assets/Plugins/Generators (its .meta carries the RoslynAnalyzer label, all
     platforms unticked).
  2. Unity batch mode: SpikeBuild.Setup (prefab + scene), then the PlayMode test (host + 1 client).
  3. Build the IL2CPP Linux Dedicated Server and a Mono Windows player.
  4. Windows host + Windows client as two processes.
  5. The Linux server in an ubuntu:22.04 container (Docker Linux engine), joined by the Windows client.
  Every step must pass; the script exits non-zero at the first failure. Logs land in logs/.

.PARAMETER Unity
  Path to the 6000.4.3f1 editor.

.PARAMETER SkipDocker
  Skip step 5 (no Docker available).
#>
param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.4.3f1\Editor\Unity.exe',
    [switch]$SkipDocker
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'Unity'
$logs = Join-Path $root 'logs'
New-Item -ItemType Directory -Force $logs | Out-Null

function Step($name) { Write-Host "== $name" -ForegroundColor Cyan }
function Fail($msg) { Write-Host "FAIL: $msg" -ForegroundColor Red; exit 1 }

function Invoke-Unity([string]$log, [string[]]$extra) {
    $a = @('-batchmode', '-nographics', '-projectPath', $project, '-logFile', (Join-Path $logs $log)) + $extra
    $p = Start-Process -FilePath $Unity -ArgumentList $a -Wait -PassThru -NoNewWindow
    return $p.ExitCode
}

function Invoke-Player([string]$exe, [string]$log, [string[]]$extra) {
    $a = @('-batchmode', '-nographics', '-logFile', (Join-Path $logs $log)) + $extra
    $p = Start-Process -FilePath $exe -ArgumentList $a -PassThru -NoNewWindow
    $null = $p.Handle   # PowerShell 5.1 only reports ExitCode for a process whose handle was taken
    return $p
}

Step 'generator: dotnet build'
dotnet build (Join-Path $root 'Generator') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { Fail 'generator build' }
Copy-Item (Join-Path $root 'Generator\bin\Release\netstandard2.0\MindAttic.Spike.Generators.dll') (Join-Path $project 'Assets\Plugins\Generators\') -Force

Step 'unity: set up prefab + scene'
if ((Invoke-Unity 'setup.log' @('-quit', '-executeMethod', 'MindAttic.Spike.Editor.SpikeBuild.Setup')) -ne 0) { Fail 'setup (see logs/setup.log)' }
$dll = [IO.File]::ReadAllText((Join-Path $project 'Library\ScriptAssemblies\MindAttic.Spike.Game.dll'), [Text.Encoding]::ASCII)
foreach ($marker in @('PingRpc', '__TotalNetVar', '__rpc_handler_', '__initializeVariables')) {
    if (-not $dll.Contains($marker)) { Fail "MindAttic.Spike.Game.dll has no $marker (generator or ILPP did not run)" }
}
Write-Host 'generated PingRpc + NetworkVariable present; NGO ILPP added __rpc_handler_ and __initializeVariables'

Step 'unity: PlayMode test, host + 1 client'
$results = Join-Path $logs 'playmode-results.xml'
if ((Invoke-Unity 'playmode.log' @('-runTests', '-testPlatform', 'PlayMode', '-assemblyNames', 'MindAttic.Spike.Tests', '-testResults', $results)) -ne 0) { Fail 'PlayMode tests (see logs/playmode-results.xml)' }

Step 'unity: IL2CPP Linux Dedicated Server build'
if ((Invoke-Unity 'build-linux-server.log' @('-quit', '-buildTarget', 'Linux64', '-standaloneBuildSubtarget', 'Server', '-executeMethod', 'MindAttic.Spike.Editor.SpikeBuild.BuildLinuxServer')) -ne 0) { Fail 'Linux server build' }

Step 'unity: Mono Windows player build'
if ((Invoke-Unity 'build-windows-player.log' @('-quit', '-buildTarget', 'Win64', '-executeMethod', 'MindAttic.Spike.Editor.SpikeBuild.BuildWindowsPlayer')) -ne 0) { Fail 'Windows player build' }
$player = Join-Path $project 'Builds\Windows\SpikePlayer.exe'

Step 'standalone: Windows host + Windows client'
$hostProc = Invoke-Player $player 'win-host.log' @('-spikeMode', 'host', '-spikePort', '7790', '-spikeTimeout', '60')
Start-Sleep -Seconds 4
$client = Invoke-Player $player 'win-client.log' @('-spikeMode', 'client', '-spikePort', '7790', '-spikeTimeout', '45')
$client.WaitForExit(); $hostProc.WaitForExit()
if ($client.ExitCode -ne 0 -or $hostProc.ExitCode -ne 0) { Fail "host/client exit $($hostProc.ExitCode)/$($client.ExitCode)" }

if ($SkipDocker) { Write-Host 'PASS (Docker step skipped)' -ForegroundColor Green; exit 0 }

Step 'docker: IL2CPP Linux server in ubuntu:22.04, Windows client joins'
$ErrorActionPreference = 'Continue'   # docker writes progress to stderr; exit codes are checked instead
$engine = (docker version --format '{{.Server.Os}}') 2>$null
$switched = $false
if ($engine -ne 'linux') {
    & 'C:\Program Files\Docker\Docker\DockerCli.exe' -SwitchLinuxEngine
    $switched = $true
    for ($i = 0; $i -lt 60 -and ((docker version --format '{{.Server.Os}}') 2>$null) -ne 'linux'; $i++) { Start-Sleep 5 }
}
try {
    docker rm -f um-t4-spike 2>$null | Out-Null
    $build = Join-Path $project 'Builds\LinuxServer'
    docker run -d --name um-t4-spike -p 7777:7777/udp -v "${build}:/build:ro" ubuntu:22.04 sh -c 'cp -r /build /srv/server && chmod +x /srv/server/SpikeServer.x86_64 && /srv/server/SpikeServer.x86_64 -batchmode -nographics -logFile - -spikeMode server -spikePort 7777 -spikeTimeout 90' | Out-Null
    Start-Sleep -Seconds 8
    $client = Invoke-Player $player 'win-client-vs-linux.log' @('-spikeMode', 'client', '-spikeAddress', '127.0.0.1', '-spikePort', '7777', '-spikeTimeout', '45')
    $client.WaitForExit()
    $serverExit = [int](docker wait um-t4-spike)
    cmd /c "docker logs um-t4-spike > `"$(Join-Path $logs 'linux-server.log')`" 2>&1"
    docker rm -f um-t4-spike | Out-Null
    if ($client.ExitCode -ne 0 -or $serverExit -ne 0) { Fail "linux server/windows client exit $serverExit/$($client.ExitCode)" }
}
finally {
    if ($switched) { & 'C:\Program Files\Docker\Docker\DockerCli.exe' -SwitchWindowsEngine }
}

Write-Host 'PASS: generator -> ILPP -> host+client -> IL2CPP Linux server' -ForegroundColor Green
