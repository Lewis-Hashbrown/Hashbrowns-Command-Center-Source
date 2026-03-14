param(
    [int]$ExpectedClients = 3,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dashboardProject = Join-Path $repoRoot "ClientDashboard\ClientDashboard.csproj"
$autoTestProject = Join-Path $repoRoot "AutoTest\AutoTest.csproj"
$dashboardExe = Join-Path $repoRoot "ClientDashboard\bin\Debug\net8.0-windows\ClientDashboard.exe"
$statusFile = Join-Path $env:TEMP "clientdashboard-live-status.json"

if (Test-Path $statusFile) {
    Remove-Item $statusFile -Force
}

Write-Host "Building projects..."
dotnet build $dashboardProject -c Debug | Out-Host
dotnet build $autoTestProject -c Debug | Out-Host

if (-not (Test-Path $dashboardExe)) {
    throw "Dashboard exe not found: $dashboardExe"
}

$dashboardProc = $null
try {
    Write-Host "Starting dashboard exe with --autolaunch..."
    $dashboardProc = Start-Process -FilePath $dashboardExe `
        -ArgumentList @("--autolaunch") `
        -PassThru

    Start-Sleep -Seconds 5
    if ($dashboardProc.HasExited) {
        throw "Dashboard exited immediately (exit code $($dashboardProc.ExitCode))."
    }

    Write-Host "Running live embed validation for $ExpectedClients client(s), timeout ${TimeoutSeconds}s..."
    dotnet run --project $autoTestProject -- live $ExpectedClients $TimeoutSeconds
    $exitCode = $LASTEXITCODE
}
finally {
    if ($dashboardProc -and -not $dashboardProc.HasExited) {
        Stop-Process -Id $dashboardProc.Id -Force -ErrorAction SilentlyContinue
    }

    Get-Process -Name "ClientDashboard" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

exit $exitCode
