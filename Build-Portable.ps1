param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $repoRoot "ClientDashboard\ClientDashboard.csproj"
$outputDir = Join-Path $repoRoot "HashBrowns-Command-Center"

Write-Host "Publishing HashBrowns Command Center ($Configuration)..."
dotnet publish $project -c $Configuration -o $outputDir

$exePath = Join-Path $outputDir "HashBrowns Command Center.exe"
if (-not (Test-Path $exePath)) {
    throw "Publish completed, but exe not found at: $exePath"
}

Write-Host ""
Write-Host "Done."
Write-Host "Run this exe:"
Write-Host $exePath
