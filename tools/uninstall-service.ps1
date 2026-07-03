#requires -RunAsAdministrator
<#
    Stops and removes the KidsMonitorService installed by install-service.ps1.
#>

$ErrorActionPreference = "Stop"
$serviceName = "KidsMonitorService"

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Service '$serviceName' is not installed."
    exit 0
}

if ($existing.Status -ne 'Stopped') {
    Write-Host "Stopping service '$serviceName'..."
    Stop-Service -Name $serviceName -Force
}

Write-Host "Deleting service '$serviceName'..."
sc.exe delete $serviceName | Out-Null

Write-Host "Done."
