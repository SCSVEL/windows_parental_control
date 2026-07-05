#requires -RunAsAdministrator
<#
    Publishes self-contained Release builds of Service/Tray/Overlay, then builds the WiX MSI.
    Requires elevation only because it stops a locally-installed KidsMonitorService first
    (harmless if it isn't installed) so its files aren't locked during publish.
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

$existing = Get-Service -Name "KidsMonitorService" -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Write-Host "Stopping KidsMonitorService so its files aren't locked during publish..."
    Stop-Service -Name "KidsMonitorService" -Force
}

Write-Host "Publishing KidsMonitor.Service ($Runtime, $Configuration)..."
dotnet publish (Join-Path $repoRoot "src\KidsMonitor.Service\KidsMonitor.Service.csproj") -c $Configuration -r $Runtime --self-contained true
if ($LASTEXITCODE -ne 0) { throw "Service publish failed." }

Write-Host "Publishing KidsMonitor.Tray ($Runtime, $Configuration)..."
dotnet publish (Join-Path $repoRoot "src\KidsMonitor.Tray\KidsMonitor.Tray.csproj") -c $Configuration -p:PublishProfile=$Runtime
if ($LASTEXITCODE -ne 0) { throw "Tray publish failed." }

Write-Host "Publishing KidsMonitor.Overlay ($Runtime, $Configuration)..."
dotnet publish (Join-Path $repoRoot "src\KidsMonitor.Overlay\KidsMonitor.Overlay.csproj") -c $Configuration -p:PublishProfile=$Runtime
if ($LASTEXITCODE -ne 0) { throw "Overlay publish failed." }

Write-Host "Building the MSI..."
dotnet build (Join-Path $repoRoot "src\KidsMonitor.Installer\KidsMonitor.Installer.wixproj") -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }

$msiPath = Join-Path $repoRoot "src\KidsMonitor.Installer\bin\x64\$Configuration\KidsMonitor.msi"
$zipPath = Join-Path $repoRoot "src\KidsMonitor.Installer\bin\x64\$Configuration\KidsMonitor.zip"

Write-Host "Zipping the MSI for download/distribution..."
Compress-Archive -Path $msiPath -DestinationPath $zipPath -Force

Write-Host "Done. MSI at $msiPath"
Write-Host "Zipped download at $zipPath"
