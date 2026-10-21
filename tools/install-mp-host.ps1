<#
.SYNOPSIS
    Packs mp-host (src/Hosting.Cli) as the MindAttic.Unity.Multiplayer.Hosting.Cli .NET tool into C:\LocalNuGet and
    installs it globally, so `mp-host game local --project <any Unity.Multiplayer game>` works from anywhere (UM-T58).
.DESCRIPTION
    The version comes from the csproj (HOUSE-LAW-1: whole numbers, major-only bumps). Packing the same version again
    overwrites the local package, so the cached copy is dropped and the tool reinstalled to pick it up.
.PARAMETER Feed
    The local NuGet folder to pack into. Default C:\LocalNuGet.
.PARAMETER SkipTests
    Pack without running the Hosting.Cli tests first.
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\install-mp-host.ps1
#>
param(
    [string]$Feed = 'C:\LocalNuGet',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$id = 'MindAttic.Unity.Multiplayer.Hosting.Cli'

if (-not $SkipTests) {
    dotnet test (Join-Path $repo 'tests\Hosting.Cli.Tests\Hosting.Cli.Tests.csproj') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Hosting.Cli tests failed; not packing.' }
}

New-Item -ItemType Directory -Force $Feed | Out-Null
dotnet pack (Join-Path $repo 'src\Hosting.Cli\Hosting.Cli.csproj') --configuration Release --output $Feed --nologo
if ($LASTEXITCODE -ne 0) { throw 'Pack failed.' }

# A same-version repack isn't picked up from the NuGet cache, and `tool update` skips a same version, so reinstall.
$cache = Join-Path $env:USERPROFILE ".nuget\packages\$($id.ToLowerInvariant())"
if (Test-Path $cache) { Remove-Item $cache -Recurse -Force }
if (dotnet tool list --global | Select-String -Pattern "^$([regex]::Escape($id.ToLowerInvariant()))\s") {
    dotnet tool uninstall --global $id | Out-Null
}
dotnet tool install --global $id --add-source $Feed
if ($LASTEXITCODE -ne 0) { throw 'Tool install failed.' }

Write-Host "Installed: $(Get-Command mp-host -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)"
