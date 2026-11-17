# Windows twin of preview.sh: the made-up Northlight school on http://localhost:5299 for a look.
# Sign in as admin / editor / coach with the passphrase "northlight admin" (or editor, coach).
# Development only; never the live file. Ctrl+C stops it.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$port = if ($env:PORT) { $env:PORT } else { '5299' }
$busy = $false
try { Invoke-WebRequest "http://127.0.0.1:$port/healthz" -UseBasicParsing -TimeoutSec 2 | Out-Null; $busy = $true } catch { }
if ($busy) { Write-Error "Something is already answering on port $port. Close the other window first." }
if (-not (dotnet --list-sdks | Select-String '^10\.')) {
    Write-Host "This needs the .NET 10 SDK, and this PC has only:"; dotnet --list-sdks
    Write-Host "Install it with:  winget install Microsoft.DotNet.SDK.10   then open a new PowerShell window and run this again."
    exit 1
}
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Store__Path = Join-Path (Get-Location) '.local-store-preview'
$env:Store__Create = 'true'
$env:Preview__Seed = 'true'
$env:Preview__Accounts = 'true'
Write-Host "Starting... when it says 'Now listening', open http://localhost:$port and sign in as admin / northlight admin"
dotnet run --project src/Cccc.Web --no-launch-profile --urls "http://127.0.0.1:$port"
