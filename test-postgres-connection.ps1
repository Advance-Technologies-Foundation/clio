# PostgreSQL Connection Diagnostic Script
# This script helps diagnose connection issues with restore-db command

Write-Host "=== PostgreSQL Connection Diagnostics ===" -ForegroundColor Cyan
Write-Host ""

# 1. Check PostgreSQL Service
Write-Host "1. Checking PostgreSQL Service Status..." -ForegroundColor Yellow
$pgService = Get-Service -Name "postgresql*" -ErrorAction SilentlyContinue
if ($pgService) {
    Write-Host "   [OK] PostgreSQL service found: $($pgService.DisplayName)" -ForegroundColor Green
    Write-Host "   Status: $($pgService.Status)" -ForegroundColor $(if ($pgService.Status -eq 'Running') { 'Green' } else { 'Red' })
} else {
    Write-Host "   [WARN] No PostgreSQL service found" -ForegroundColor Yellow
}
Write-Host ""

# 2. Test Port Connectivity
Write-Host "2. Testing Port 5432 Connectivity..." -ForegroundColor Yellow
$portTest = Test-NetConnection -ComputerName localhost -Port 5432 -WarningAction SilentlyContinue
if ($portTest.TcpTestSucceeded) {
    Write-Host "   [OK] Port 5432 is accessible" -ForegroundColor Green
} else {
    Write-Host "   [ERROR] Port 5432 is not accessible" -ForegroundColor Red
}
Write-Host ""

# 3. Check appsettings.json
Write-Host "3. Checking appsettings.json Configuration..." -ForegroundColor Yellow
$appSettingsPath = "$env:USERPROFILE\.clio\appsettings.json"
if (Test-Path $appSettingsPath) {
    Write-Host "   [OK] appsettings.json found at: $appSettingsPath" -ForegroundColor Green
    try {
        $config = Get-Content $appSettingsPath | ConvertFrom-Json
        if ($config.db) {
            Write-Host "   [OK] Database configurations found:" -ForegroundColor Green
            $config.db.PSObject.Properties | ForEach-Object {
                Write-Host "      - $($_.Name): $($_.Value.dbType) @ $($_.Value.hostname):$($_.Value.port)" -ForegroundColor Cyan
            }
        } else {
            Write-Host "   [WARN] No 'db' section found in appsettings.json" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "   [ERROR] Failed to parse appsettings.json: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "   [ERROR] appsettings.json not found at: $appSettingsPath" -ForegroundColor Red
    Write-Host "   You need to create this file with database configuration" -ForegroundColor Yellow
}
Write-Host ""

# 4. Check PostgreSQL Data Directory
Write-Host "4. Finding PostgreSQL Installation..." -ForegroundColor Yellow
$pgPaths = @(
    "C:\Program Files\PostgreSQL",
    "C:\PostgreSQL"
)
foreach ($path in $pgPaths) {
    if (Test-Path $path) {
        Write-Host "   [OK] PostgreSQL found at: $path" -ForegroundColor Green
        $versions = Get-ChildItem -Path $path -Directory -ErrorAction SilentlyContinue
        foreach ($ver in $versions) {
            Write-Host "      Version: $($ver.Name)" -ForegroundColor Cyan
            $pgHbaPath = Join-Path $ver.FullName "data\pg_hba.conf"
            if (Test-Path $pgHbaPath) {
                Write-Host "      pg_hba.conf: $pgHbaPath" -ForegroundColor Cyan
            }
        }
    }
}
Write-Host ""

# 5. Recommendations
Write-Host "=== Recommendations ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "If you're getting 'Exception while reading from stream' error:" -ForegroundColor Yellow
Write-Host "1. Verify your PostgreSQL password is correct" -ForegroundColor White
Write-Host "2. Check pg_hba.conf allows password authentication (md5 or scram-sha-256)" -ForegroundColor White
Write-Host "3. Ensure postgresql.conf has: listen_addresses = '*' or 'localhost'" -ForegroundColor White
Write-Host "4. Your appsettings.json should look like:" -ForegroundColor White
Write-Host @"
{
  "db": {
    "my-local-postgres": {
      "dbType": "postgres",
      "hostname": "localhost",
      "port": 5432,
      "username": "postgres",
      "password": "YOUR_PASSWORD_HERE",
      "description": "Local PostgreSQL Server"
    }
  }
}
"@ -ForegroundColor Gray
Write-Host ""
Write-Host "5. Test the connection with:" -ForegroundColor White
Write-Host "   clio restore-db --dbServerName my-local-postgres --dbName testdb --backupPath path\to\backup.backup --drop-if-exists" -ForegroundColor Cyan
