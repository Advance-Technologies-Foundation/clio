# Live interaction soak (NOT headless): drives the real window through env-load + a clio run +
# N show/hide/tray/hotkey cycles and asserts NO StackOverflow (process exits 0) and exactly ONE
# WndProc hook install. This exercises the click/command path that the "alive at rest" check missed.
param(
    [string]$Config = "Debug",
    [int]$Cycles = 50
)

$ErrorActionPreference = "Stop"
$exe = "C:\Projects\clio-ring-spike-claude\ClioLauncher.Desktop\bin\$Config\net10.0\ClioLauncher.Desktop.exe"
$log = "$env:LOCALAPPDATA\clio-ring\startup.log"

if (-not (Test-Path $exe)) { Write-Error "exe not found: $exe (build $Config first)"; exit 2 }

Get-Process ClioLauncher.Desktop -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
if (Test-Path $log) { Remove-Item $log -Force }

$p = Start-Process $exe -ArgumentList @("--soak", "$Cycles") -PassThru
if (-not $p.WaitForExit(30000)) {
    $p.Kill()
    Write-Host "SOAK FAIL ($Config): TIMEOUT (did not finish $Cycles cycles in 30s)"
    exit 1
}

$exit = $p.ExitCode
Write-Host "soak exit=$exit (0=pass, 1=exception, 3=hook-install-count!=1)"
Write-Host "--- startup.log (soak lines) ---"
if (Test-Path $log) { Get-Content $log | Select-String -Pattern "soak|hotkey|FATAL|FAILED" }

if ($exit -eq 0) { Write-Host "SOAK PASS ($Config): $Cycles cycles, no overflow, single hook install"; exit 0 }
else { Write-Host "SOAK FAIL ($Config)"; exit 1 }
