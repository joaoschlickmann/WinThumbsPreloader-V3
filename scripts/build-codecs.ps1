<#
.SYNOPSIS
    Downloads/verifies PDFium (chromium/8165, non-V8/non-XFA) and builds the native
    WinThumbsCodecs Rust COM shell extension DLL (x86_64-pc-windows-msvc).

.EXAMPLE
    .\scripts\build-codecs.ps1
    .\scripts\build-codecs.ps1 -Configuration Release -RunTests -CopyToOutput
    .\scripts\build-codecs.ps1 -FetchPdfiumOnly
#>
[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [switch]$RunTests,
    [switch]$CopyToOutput,
    [switch]$NoCopyToOutput,
    [switch]$SkipPdfiumDownload,
    [switch]$FetchPdfiumOnly
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$pdfiumTag = "chromium/8165"
$pdfiumUrl = "https://github.com/bblanchon/pdfium-binaries/releases/download/$pdfiumTag/pdfium-win-x64.tgz"
$pdfiumExpectedSha256 = "ca7338ae7779b8b1b82f4580305c4d762d214f8df1d54b47647fe3cc75989ded"

$cacheDir = Join-Path $repoRoot "artifacts\cache"
$archivePath = Join-Path $cacheDir "pdfium-win-x64-chromium-8165.tgz"
$prebuiltDir = Join-Path $repoRoot "WinThumbsCodecs\prebuilt\win-x64"
$pdfiumDllPath = Join-Path $prebuiltDir "pdfium.dll"
$pdfiumLicensePath = Join-Path $prebuiltDir "PDFIUM-LICENSE.txt"

function Assert-Sha256 {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string]$ExpectedHash
    )

    if (-not (Test-Path -LiteralPath $FilePath)) {
        throw "Expected file missing for SHA-256 verification: $FilePath"
    }

    $actualHash = (Get-FileHash -LiteralPath $FilePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $ExpectedHash.ToLowerInvariant()) {
        throw "SHA-256 verification failed for '$FilePath'. Expected '$ExpectedHash', got '$actualHash'."
    }
}

function Ensure-PdfiumPrebuilt {
    New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
    New-Item -ItemType Directory -Path $prebuiltDir -Force | Out-Null

    $needsDownload = $true
    if (Test-Path -LiteralPath $archivePath) {
        try {
            Assert-Sha256 -FilePath $archivePath -ExpectedHash $pdfiumExpectedSha256
            $needsDownload = $false
        }
        catch {
            Write-Warning "Cached PDFium archive failed hash check; re-downloading."
            Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
        }
    }

    if ($needsDownload) {
        if ($SkipPdfiumDownload) {
            throw "PDFium archive not found or invalid at '$archivePath' and -SkipPdfiumDownload was specified."
        }

        Write-Host "Downloading PDFium ($pdfiumTag) from $pdfiumUrl..." -ForegroundColor Cyan
        $tempDownload = "$archivePath.tmp.$([Guid]::NewGuid().ToString('N'))"
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
            Invoke-WebRequest -Uri $pdfiumUrl -OutFile $tempDownload -UseBasicParsing
            Assert-Sha256 -FilePath $tempDownload -ExpectedHash $pdfiumExpectedSha256
            Move-Item -LiteralPath $tempDownload -Destination $archivePath -Force
        }
        finally {
            if (Test-Path -LiteralPath $tempDownload) {
                Remove-Item -LiteralPath $tempDownload -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Assert-Sha256 -FilePath $archivePath -ExpectedHash $pdfiumExpectedSha256

    if (-not (Test-Path -LiteralPath $pdfiumDllPath) -or -not (Test-Path -LiteralPath $pdfiumLicensePath)) {
        $extractDir = Join-Path $cacheDir ("pdfium_extract_" + [Guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
        try {
            Write-Host "Extracting bin/pdfium.dll and LICENSE..." -ForegroundColor Cyan
            & tar -xzf $archivePath -C $extractDir "bin/pdfium.dll" "LICENSE"
            if ($LASTEXITCODE -ne 0) {
                throw "tar extraction failed with exit code $LASTEXITCODE"
            }

            $extractedDll = Join-Path $extractDir "bin\pdfium.dll"
            $extractedLicense = Join-Path $extractDir "LICENSE"
            if (-not (Test-Path -LiteralPath $extractedDll) -or -not (Test-Path -LiteralPath $extractedLicense)) {
                throw "Expected files bin/pdfium.dll or LICENSE were not found inside PDFium archive."
            }

            Copy-Item -LiteralPath $extractedDll -Destination $pdfiumDllPath -Force
            Copy-Item -LiteralPath $extractedLicense -Destination $pdfiumLicensePath -Force
        }
        finally {
            Remove-Item -LiteralPath $extractDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Host "PDFium ready: $pdfiumDllPath ($([Math]::Round((Get-Item -LiteralPath $pdfiumDllPath).Length / 1MB, 2)) MB)" -ForegroundColor Green
}

function Resolve-CargoExe {
    $cmd = Get-Command cargo -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    $userCargo = Join-Path $env:USERPROFILE ".cargo\bin\cargo.exe"
    if (Test-Path -LiteralPath $userCargo) {
        return $userCargo
    }

    throw "Rust 'cargo' executable was not found on PATH or at '$userCargo'."
}

Ensure-PdfiumPrebuilt

if ($FetchPdfiumOnly) {
    Write-Host "FetchPdfiumOnly complete." -ForegroundColor Green
    exit 0
}

$manifestPath = Join-Path $repoRoot "WinThumbsCodecs\Cargo.toml"
$lockPath = Join-Path $repoRoot "WinThumbsCodecs\Cargo.lock"
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Rust manifest not found at '$manifestPath'."
}

$cargoExe = Resolve-CargoExe
$cargoArgsBase = @("--manifest-path", $manifestPath)
if (Test-Path -LiteralPath $lockPath) {
    $cargoArgsBase += "--locked"
}

if ($RunTests) {
    Write-Host "Running WinThumbsCodecs Rust tests..." -ForegroundColor Yellow
    $testArgs = @("test") + $cargoArgsBase
    & $cargoExe @testArgs
    if ($LASTEXITCODE -ne 0) {
        throw "cargo test failed with exit code $LASTEXITCODE"
    }
}

Write-Host "Building WinThumbsCodecs ($Configuration)..." -ForegroundColor Cyan
$buildArgs = @("build") + $cargoArgsBase
if ($Configuration -eq "Release") {
    $buildArgs += "--release"
}
& $cargoExe @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "cargo build failed with exit code $LASTEXITCODE"
}

$targetSubDir = if ($Configuration -eq "Release") { "release" } else { "debug" }
$builtDllPath = Join-Path $repoRoot "WinThumbsCodecs\target\$targetSubDir\WinThumbsCodecs.dll"
if (-not (Test-Path -LiteralPath $builtDllPath)) {
    throw "Expected compiled codec DLL not found at '$builtDllPath'."
}

Write-Host "Built DLL: $builtDllPath ($([Math]::Round((Get-Item -LiteralPath $builtDllPath).Length / 1MB, 2)) MB)" -ForegroundColor Green

$shouldCopy = ($CopyToOutput -or -not $NoCopyToOutput) -and -not $NoCopyToOutput
if ($shouldCopy) {
    $outCodecsDir = Join-Path $repoRoot "WinThumbsPreloader\WinThumbsPreloader\bin\$Configuration\net10.0-windows7.0\win-x64\Codecs"
    New-Item -ItemType Directory -Path $outCodecsDir -Force | Out-Null

    Copy-Item -LiteralPath $builtDllPath -Destination (Join-Path $outCodecsDir "WinThumbsCodecs.dll") -Force
    Copy-Item -LiteralPath $pdfiumDllPath -Destination (Join-Path $outCodecsDir "pdfium.dll") -Force
    Copy-Item -LiteralPath $pdfiumLicensePath -Destination (Join-Path $outCodecsDir "PDFIUM-LICENSE.txt") -Force

    Write-Host "Copied codecs payload to $outCodecsDir" -ForegroundColor Green
}
