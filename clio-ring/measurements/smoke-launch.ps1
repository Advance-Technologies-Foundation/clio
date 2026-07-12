# Real-window startup smoke test (NOT headless): launches the actual exe with a normal desktop
# lifetime in both normal-motion and reduced-motion modes and asserts each shows the window and
# exits 0 (no exception on the live render/entrance path that headless bitmap capture skips).
param(
    [string]$Config = "Debug"
)

$ErrorActionPreference = "Stop"
$exe = "C:\Projects\clio-ring-spike-claude\ClioLauncher.Desktop\bin\$Config\net10.0\ClioLauncher.Desktop.exe"
$log = "$env:LOCALAPPDATA\clio-ring\startup.log"

if (-not (Test-Path $exe)) { Write-Error "exe not found: $exe (build $Config first)"; exit 2 }

function Invoke-Smoke([string]$label, [string[]]$modeArgs) {
    Get-Process ClioLauncher.Desktop -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    $args = @("--smoke") + $modeArgs
    $p = Start-Process $exe -ArgumentList $args -PassThru
    if (-not $p.WaitForExit(8000)) {
        $p.Kill(); Write-Host "$label : TIMEOUT (window never self-exited)"; return $false
    }
    $ok = $p.ExitCode -eq 0
    Write-Host "$label : exit=$($p.ExitCode)  =>  $([bool]$ok ? 'PASS' : 'FAIL')"
    return $ok
}

$a = Invoke-Smoke "normal-motion " @()
$b = Invoke-Smoke "reduced-motion" @("--reduced-motion")

Write-Host "--- last startup.log lines ---"
if (Test-Path $log) { Get-Content $log -Tail 8 }

if ($a -and $b) { Write-Host "SMOKE PASS ($Config)"; exit 0 } else { Write-Host "SMOKE FAIL ($Config)"; exit 1 }
