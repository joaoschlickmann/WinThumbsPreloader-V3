<#
.SYNOPSIS
    Executes the full WinThumbsPreloader v3 automated test suite:
    - Unit and Regression Tests (Options, WindowsOptimizer, CacheBackup, DirectoryScanner, AutoUpdater, BulkPreset, Logger)
    - STA COM Pipeline Integration Tests (PreloaderPipelineIntegrationTests)
    - Visual and User-Simulation E2E Tests (VisualAndUserSimulationE2ETests)

.EXAMPLE
    .\scripts\run-e2e-tests.ps1
    .\scripts\run-e2e-tests.ps1 -VisualOnly
#>
[CmdletBinding()]
param(
    [switch]$VisualOnly,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$dotnetExe = "dotnet"
if (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") {
    $env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
    $dotnetExe = "$env:USERPROFILE\.dotnet\dotnet.exe"
}

Write-Host "==============================================================" -ForegroundColor Cyan
Write-Host " WinThumbsPreloader v3 - E2E, Visual and Regression Test Suite" -ForegroundColor Cyan
Write-Host "==============================================================" -ForegroundColor Cyan
Write-Host "Using .NET SDK: $dotnetExe" -ForegroundColor DarkGray

$slnPath = Join-Path $repoRoot "WinThumbsPreloader\WinThumbsPreloader.sln"
$workspaceBinPrefix = Join-Path $repoRoot "WinThumbsPreloader\"

function Stop-WorkspaceTestProcesses {
    Get-Process -Name "WinThumbsPreloader" -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            if ($_.Path -and $_.Path.StartsWith($workspaceBinPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
                $_.WaitForExit(3000) | Out-Null
            }
        } catch { }
    }
}

Stop-WorkspaceTestProcesses
$testExitCode = 0
try {
    if ($VisualOnly) {
        Write-Host "`n[1/1] Running Visual and User-Simulation E2E Tests..." -ForegroundColor Yellow
        & $dotnetExe test $slnPath -c $Configuration --filter "FullyQualifiedName~VisualAndUserSimulationE2ETests" --nologo -v minimal
    } else {
        Write-Host "`n[1/2] Running Full Automated Unit, Regression, Integration and Visual E2E Suite..." -ForegroundColor Yellow
        & $dotnetExe test $slnPath -c $Configuration --nologo -v minimal
    }
    $testExitCode = $LASTEXITCODE
} finally {
    Stop-WorkspaceTestProcesses
}

if ($testExitCode -ne 0) {
    Write-Error "Test suite failed with exit code $testExitCode."
    exit $testExitCode
}

$visualDir = Join-Path $repoRoot "artifacts\e2e-visual"
if (Test-Path $visualDir) {
    $screenshots = Get-ChildItem $visualDir -Filter "*.png" | Sort-Object Name
    Write-Host "`n[2/2] Visual E2E Screenshots Captured ($($screenshots.Count) files in artifacts/e2e-visual/):" -ForegroundColor Green
    foreach ($img in $screenshots) {
        $sizeKb = [Math]::Round($img.Length / 1KB, 1)
        Write-Host "  - $($img.Name) ($sizeKb KB)" -ForegroundColor Gray
    }
}

Write-Host "`nALL TESTS PASSED SUCCESSFULLY!" -ForegroundColor Green
