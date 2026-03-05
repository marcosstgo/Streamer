<#
Verify-FFmpeg.ps1

Usage:
  .\verify-ffmpeg.ps1 [-BinDir <path>]

Defaults:
  -BinDir: ../Streamer   (relative to this script's folder)

This script checks the SHA256 checksums of ffmpeg.exe and ffprobe.exe against the expected values
recorded in the release.
#>
param(
    [string]$BinDir = (Join-Path $PSScriptRoot "..\Streamer")
)

$expected = @{
    "ffmpeg.exe"  = "5AF82A0D4FE2B9EAE211B967332EA97EDFC51C6B328CA35B827E73EAC560DC0D";
    "ffprobe.exe" = "192A1D6899059765AC8C39764FC3148D4E6049955956DC2029F81F4BD6A8972D";
}

Write-Host "Verifying FFmpeg binaries in: $BinDir"
$allOk = $true

foreach ($name in $expected.Keys) {
    $path = Join-Path $BinDir $name
    if (-Not (Test-Path $path)) {
        Write-Host "[ERROR] File not found: $path" -ForegroundColor Red
        $allOk = $false
        continue
    }

    try {
        $h = Get-FileHash -Algorithm SHA256 -Path $path
        $actual = $h.Hash.ToUpper()
        $expect = $expected[$name].ToUpper()
        if ($actual -eq $expect) {
            Write-Host "[OK]     $name : $actual" -ForegroundColor Green
        } else {
            Write-Host "[MISMATCH] $name" -ForegroundColor Yellow
            Write-Host "  Expected: $expect"
            Write-Host "  Actual:   $actual"
            $allOk = $false
        }
    }
    catch {
        Write-Host "[ERROR] Failed to compute hash for $path : $_" -ForegroundColor Red
        $allOk = $false
    }
}

if ($allOk) {
    Write-Host "All checksums match." -ForegroundColor Green
    exit 0
} else {
    Write-Host "One or more checks failed. Do not distribute or use these binaries until resolved." -ForegroundColor Red
    exit 2
}
