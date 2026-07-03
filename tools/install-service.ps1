#requires -RunAsAdministrator
<#
    Fast dev-loop installer for KidsMonitor.Service.
    Builds the Service project and registers/starts it as a Windows Service via sc.exe.
    Not a substitute for the WiX MSI (milestone M4) -- dev iteration only.
#>
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$serviceProject = Join-Path $repoRoot "src\KidsMonitor.Service\KidsMonitor.Service.csproj"
$serviceName = "KidsMonitorService"

Write-Host "Building KidsMonitor.Service ($Configuration)..."
dotnet build $serviceProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Build failed."
}

$publishDir = Join-Path $repoRoot "src\KidsMonitor.Service\bin\$Configuration\net8.0-windows"
$exePath = Join-Path $publishDir "KidsMonitor.Service.exe"

if (-not (Test-Path $exePath)) {
    throw "Could not find built service exe at $exePath"
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service '$serviceName' already exists -- stopping and removing it first."
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
    }
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 1
}

Write-Host "Creating service '$serviceName' -> $exePath"
sc.exe create $serviceName binPath= "`"$exePath`"" start= auto obj= LocalSystem | Out-Null

Write-Host "Starting service '$serviceName'..."
Start-Service -Name $serviceName

Start-Sleep -Seconds 2
Get-Service -Name $serviceName | Format-List Name, Status, StartType

$logDir = "C:\ProgramData\KidsMonitor\logs"
Write-Host "Service logs: $logDir"
