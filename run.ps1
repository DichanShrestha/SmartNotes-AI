# SmartNotes AI - Launch Script for Visual Studio Code & Windows
$ErrorActionPreference = "Stop"

Write-Host "==============================================" -ForegroundColor Cyan
Write-Host "  Starting SmartNotes AI Web Application...   " -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan

# 1. Ensure SQL Server LocalDB is running
Write-Host "[1/4] Ensuring LocalDB (MSSQLLocalDB) is running..." -ForegroundColor Yellow
try {
    $info = sqllocaldb info MSSQLLocalDB 2>&1
    if ($info -match "is not created") {
        Write-Host "Creating MSSQLLocalDB instance..." -ForegroundColor Gray
        sqllocaldb create MSSQLLocalDB
    }
    sqllocaldb start MSSQLLocalDB | Out-Null
    Write-Host "LocalDB is ready." -ForegroundColor Green
} catch {
    Write-Warning "Could not manage LocalDB automatically: $_"
}

# 2. Stop any previously running IIS Express process
Write-Host "[2/4] Checking for previous IIS Express processes..." -ForegroundColor Yellow
Get-Process iisexpress -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

# 3. Build Solution
Write-Host "[3/4] Building solution SmartNotesAI.sln..." -ForegroundColor Yellow
$buildResult = dotnet build "$PSScriptRoot\SmartNotesAI.sln" -c Debug
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed! Please resolve compilation errors above."
    exit $LASTEXITCODE
}
Write-Host "Build succeeded." -ForegroundColor Green

# 4. Launch IIS Express
$webPath = Join-Path $PSScriptRoot "Web"
$iisExpressPath = "C:\Program Files\IIS Express\iisexpress.exe"
if (-not (Test-Path $iisExpressPath)) {
    $iisExpressPath = "C:\Program Files (x86)\IIS Express\iisexpress.exe"
}

if (-not (Test-Path $iisExpressPath)) {
    Write-Error "IIS Express executable not found at $iisExpressPath!"
    exit 1
}

Write-Host "[4/4] Starting IIS Express on http://localhost:5000 ..." -ForegroundColor Yellow
$appUrl = "http://localhost:5000/login.html"

# Start IIS Express in background job or process
$processInfo = New-Object System.Diagnostics.ProcessStartInfo
$processInfo.FileName = $iisExpressPath
$processInfo.Arguments = "/path:`"$webPath`" /port:5000"
$processInfo.UseShellExecute = $true
$iisProcess = [System.Diagnostics.Process]::Start($processInfo)

Start-Sleep -Seconds 2

# Open in default browser
Write-Host "Opening $appUrl in browser..." -ForegroundColor Green
Start-Process $appUrl

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  SmartNotes AI is running at: http://localhost:5000/      " -ForegroundColor Green
Write-Host "  Login Page:                  $appUrl                     " -ForegroundColor Green
Write-Host "  API Endpoint:                http://localhost:5000/api/  " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Press Ctrl+C or close this window to stop the server." -ForegroundColor Gray

# Wait for process exit
try {
    $iisProcess.WaitForExit()
} finally {
    if (-not $iisProcess.HasExited) {
        $iisProcess.Kill()
    }
}
