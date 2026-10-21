<#
.SYNOPSIS
  Sets the GitHub Actions secrets a Unity.Multiplayer game's CI and nightly smoke need, on every repo given.

.DESCRIPTION
  UNITY_LICENSE comes from this PC's activated Unity license file; UNITY_EMAIL and UNITY_PASSWORD are asked
  for once. The Unity.Multiplayer repo also gets a generated RENDEZVOUS_SMOKE_SECRET if it has none.
  Values go to `gh` on stdin, never on the command line. Needs the GitHub CLI logged in with 'repo' scope.

.EXAMPLE
  powershell -File tools\set-ci-secrets.ps1
  powershell -File tools\set-ci-secrets.ps1 -Repos mindattic/MyNewGame
#>
param(
    [string[]]$Repos = @('mindattic/Unity.Multiplayer', 'mindattic/Unity.Cursory'),
    [string]$LicenseFile = 'C:\ProgramData\Unity\Unity_lic.ulf'
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = New-Object System.Text.UTF8Encoding $false

function Set-Secret([string]$Repo, [string]$Name, [string]$Value) {
    $Value | gh secret set $Name -R $Repo
    if ($LASTEXITCODE -ne 0) { throw "Setting $Name on $Repo failed." }
    Write-Host "  $Name set" -ForegroundColor Green
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'The GitHub CLI (gh) is not installed.' }
gh auth status *> $null
if ($LASTEXITCODE -ne 0) { throw "gh is not logged in: run 'gh auth login' first." }
if (-not (Test-Path $LicenseFile)) {
    throw "No Unity license at $LicenseFile. Open Unity Hub, sign in and activate a license, then run this again (or pass -LicenseFile)."
}

$license = Get-Content -Raw $LicenseFile
$email = Read-Host 'Unity account email'
$secure = Read-Host 'Unity account password' -AsSecureString
$password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
if (-not $email -or -not $password) { throw 'Email and password are both required.' }

foreach ($repo in $Repos) {
    Write-Host $repo -ForegroundColor Cyan
    Set-Secret $repo 'UNITY_LICENSE' $license
    Set-Secret $repo 'UNITY_EMAIL' $email
    Set-Secret $repo 'UNITY_PASSWORD' $password

    if ($repo -like '*/Unity.Multiplayer') {
        $existing = gh secret list -R $repo --json name --jq '.[].name'
        if ($existing -notcontains 'RENDEZVOUS_SMOKE_SECRET') {
            $bytes = New-Object byte[] 24
            [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
            Set-Secret $repo 'RENDEZVOUS_SMOKE_SECRET' (($bytes | ForEach-Object { $_.ToString('x2') }) -join '')
        }
    }
}

Write-Host "`nDone. Secrets now on each repo:" -ForegroundColor Cyan
foreach ($repo in $Repos) {
    Write-Host $repo
    gh secret list -R $repo
}
