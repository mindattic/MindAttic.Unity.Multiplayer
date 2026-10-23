<#
.SYNOPSIS
  Links a Unity project to a Unity Cloud project and adds com.unity.services.multiplayer, so Unity Relay join codes
  work for a game hosted at home with no port forwarding (UM-T71).

.DESCRIPTION
  Relay needs three things: the com.unity.services.multiplayer package (it defines MINDATTIC_UGS), the project linked
  to a Unity Cloud project (a cloud project id, which Unity bakes into every build and Unity Services reads as
  Application.cloudProjectId), and Relay turned on for that Cloud project at https://cloud.unity.com/.

  This script does the first two with the editor closed:
    - writes cloudProjectId / projectName / organizationId in ProjectSettings/ProjectSettings.asset (what Project
      Settings > Services > Link writes), and
    - adds "com.unity.services.multiplayer": "<version>" to Packages/manifest.json when it is missing.
  It refuses while the Unity editor has the project open (the editor would overwrite ProjectSettings.asset); close it
  first. Turning Relay on is a dashboard switch and stays a manual step: the script prints where.

  Find the ids at https://cloud.unity.com/ > the project > Settings (Project ID, and the organization's ID under the
  organization's settings), or in Unity Hub's project list. With -Check it only reports where the project stands.

.PARAMETER Project
  The Unity project folder (the one holding Assets, Packages and ProjectSettings).

.PARAMETER ProjectId
  The Unity Cloud project id (a GUID).

.PARAMETER OrganizationId
  The Unity organization id the Cloud project belongs to (shown as Organization ID in the dashboard).

.PARAMETER ProjectName
  The Cloud project's name, shown in Project Settings > Services. Optional.

.PARAMETER PackageVersion
  The com.unity.services.multiplayer version to add (default 2.4.1, the version Unity.Multiplayer is verified with).

.PARAMETER NoPackage
  Link only; leave Packages/manifest.json alone.

.PARAMETER Check
  Report the package line and the link, change nothing. Exits 0 when both are in place, 1 when not.

.EXAMPLE
  powershell -File tools/link-unity-cloud.ps1 -Project D:\Projects\MindAttic\Unity.Cursory -ProjectId 0f8fad5b-d9cb-469f-a165-70867728950e -OrganizationId 1234567890123 -ProjectName Cursory

.EXAMPLE
  powershell -File tools/link-unity-cloud.ps1 -Project D:\Projects\MindAttic\Unity.Cursory -Check
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Project,
    [string]$ProjectId = '',
    [string]$OrganizationId = '',
    [string]$ProjectName = '',
    [string]$PackageVersion = '2.4.1',
    [switch]$NoPackage,
    [switch]$Check
)
$ErrorActionPreference = 'Stop'
$package = 'com.unity.services.multiplayer'
$dashboard = 'https://cloud.unity.com/'
$utf8 = New-Object System.Text.UTF8Encoding($false)

$Project = (Resolve-Path -LiteralPath $Project).Path
$settingsPath = Join-Path $Project 'ProjectSettings\ProjectSettings.asset'
$manifestPath = Join-Path $Project 'Packages\manifest.json'
foreach ($p in @($settingsPath, $manifestPath)) {
    if (-not (Test-Path -LiteralPath $p)) { throw "Not a Unity project: $p is missing." }
}

function Get-SettingValue([string]$text, [string]$key) {
    $m = [regex]::Match($text, "(?m)^[ \t]*$([regex]::Escape($key)):[ \t]*([^\r\n]*?)[ \t]*\r?$")
    if ($m.Success) { return $m.Groups[1].Value } else { return $null }
}

function Set-SettingValue([string]$text, [string]$key, [string]$value) {
    $re = New-Object System.Text.RegularExpressions.Regex("(?m)^([ \t]*$([regex]::Escape($key)):)[ \t]*[^\r\n]*")
    if (-not $re.IsMatch($text)) { throw "ProjectSettings.asset has no '${key}:' line; open the project in Unity once so it writes one." }
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator] { param($m) $m.Groups[1].Value + ' ' + $value }
    return $re.Replace($text, $evaluator, 1)
}

function Test-ManifestHasPackage([string]$json) {
    return [regex]::IsMatch($json, '"' + [regex]::Escape($package) + '"\s*:')
}

$settings = [System.IO.File]::ReadAllText($settingsPath)
$manifest = [System.IO.File]::ReadAllText($manifestPath)
$currentId = Get-SettingValue $settings 'cloudProjectId'
$hasPackage = Test-ManifestHasPackage $manifest

if ($Check) {
    Write-Host "Project:  $Project"
    Write-Host ("Package:  " + $(if ($hasPackage) { "$package is in Packages/manifest.json" } else { "$package is NOT in Packages/manifest.json" }))
    if ([string]::IsNullOrWhiteSpace($currentId)) {
        Write-Host "Link:     not linked to a Unity Cloud project (cloudProjectId is empty)"
    } else {
        Write-Host "Link:     cloudProjectId $currentId, projectName '$(Get-SettingValue $settings 'projectName')', organizationId '$(Get-SettingValue $settings 'organizationId')'"
    }
    Write-Host "Relay:    turn it on for the Cloud project at $dashboard (Products > Relay)"
    if ($hasPackage -and -not [string]::IsNullOrWhiteSpace($currentId)) { exit 0 } else { exit 1 }
}

$guid = [guid]::Empty
if (-not [guid]::TryParse($ProjectId.Trim(), [ref]$guid)) {
    throw "-ProjectId '$ProjectId' is not a Unity Cloud project id (a GUID, e.g. 0f8fad5b-d9cb-469f-a165-70867728950e). Find it at $dashboard > the project > Settings."
}
if ([string]::IsNullOrWhiteSpace($OrganizationId)) {
    throw "-OrganizationId is required (the Organization ID in the dashboard's organization settings)."
}

# The editor holds Temp/UnityLockfile open while the project is loaded, and would overwrite ProjectSettings.asset.
$lock = Join-Path $Project 'Temp\UnityLockfile'
if (Test-Path -LiteralPath $lock) {
    try {
        $stream = [System.IO.File]::Open($lock, 'Open', 'ReadWrite', 'None')
        $stream.Close()
    } catch {
        throw "The Unity editor has $Project open. Close it, then run this again (the editor would overwrite ProjectSettings.asset)."
    }
}

$id = $guid.ToString('D')
$updated = Set-SettingValue $settings 'cloudProjectId' $id
$updated = Set-SettingValue $updated 'organizationId' $OrganizationId.Trim()
if ($ProjectName.Trim()) { $updated = Set-SettingValue $updated 'projectName' $ProjectName.Trim() }
if ($updated -ne $settings) {
    [System.IO.File]::WriteAllText($settingsPath, $updated, $utf8)
    Write-Host "Linked: $settingsPath now has cloudProjectId $id, organizationId $($OrganizationId.Trim())."
} else {
    Write-Host "Already linked to $id."
}

if (-not $NoPackage) {
    if ($hasPackage) {
        Write-Host "$package is already in $manifestPath."
    } else {
        $m = [regex]::Match($manifest, '"dependencies"\s*:\s*\{(\r?\n)?')
        if (-not $m.Success) { throw "$manifestPath has no ""dependencies"" object." }
        $newline = if ($manifest.Contains("`r`n")) { "`r`n" } else { "`n" }
        $line = '    "' + $package + '": "' + $PackageVersion + '",' + $newline
        $insertAt = $m.Index + $m.Length
        if (-not $m.Groups[1].Success) { $line = $newline + $line }
        $manifest = $manifest.Insert($insertAt, $line)
        [System.IO.File]::WriteAllText($manifestPath, $manifest, $utf8)
        Write-Host "Added to ${manifestPath}: ""$package"": ""$PackageVersion"""
    }
}

Write-Host ''
Write-Host 'Next:'
Write-Host "  1. Turn on Relay for the Cloud project: $dashboard > the project > Products > Relay."
Write-Host '  2. Open the project in Unity (signed in with an account in that organization); Project Settings > Services shows the link,'
Write-Host '     and MindAttic > Multiplayer > Check Unity Relay Setup says whether Relay is ready.'
Write-Host '  3. Leave NetBootstrap''s Join Codes at Auto: Host publishes a Relay code, Join by code resolves it.'
