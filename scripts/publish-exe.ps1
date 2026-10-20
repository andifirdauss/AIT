<#
.SYNOPSIS
    Membuat AIThread.exe standalone (self-contained, single-file) di dist\AIThread.

.DESCRIPTION
    Hasilnya berjalan di Windows x64 tanpa .NET SDK/runtime. Folder dist\AIThread berisi:
      AIThread.exe       aplikasi (menu interaktif, tray, autostart)
      appsettings.json   konfigurasi (connection string, dll.); boleh diubah
      wwwroot\, docs\    aset web dan panduan
    Build memakai folder artifacts sendiri, jadi tidak bentrok dengan bin/ yang dikunci Visual Studio atau
    instance yang sedang berjalan.

.EXAMPLE
    .\scripts\publish-exe.ps1 -Zip
#>
[CmdletBinding()]
param(
    # Buat juga dist\AIThread-win-x64.zip.
    [switch]$Zip,

    # Folder hasil publish. Default dist\AIThread; pakai folder lain bila exe di sana sedang berjalan.
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot 'src\AIThread.UI\AIThread.UI.csproj'
$Dist = Join-Path $RepoRoot 'dist'
$Output = if ($OutputDir) { $(if ([IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path (Get-Location).Path $OutputDir }) } else { Join-Path $Dist 'AIThread' }
$Artifacts = Join-Path $Dist '.build'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK (dotnet) tidak ditemukan di PATH.' }

# A running exe is locked: stop before anything is deleted, so a live instance keeps its wwwroot and docs.
$running = Get-Process AIThread -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($Output, [StringComparison]::OrdinalIgnoreCase) }
if ($running) {
    throw "AIThread.exe di $Output sedang berjalan (PID $($running.Id -join ', ')). Matikan dulu ('AIThread.exe stop') atau pakai -OutputDir folder lain."
}

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

Write-Host "Publish AIThread.exe ke $Output ..."
& dotnet publish $Project -p:PublishProfile=standalone "-p:PublishDir=$Output/" --artifacts-path $Artifacts --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet publish gagal (exit $LASTEXITCODE)." }

# Debug symbols, the IIS web.config, and the development settings are not part of a standalone install.
Get-ChildItem $Output -Include '*.pdb', 'web.config', 'appsettings.Development.json' -File -Recurse | Remove-Item -Force

$exe = Join-Path $Output 'AIThread.exe'
if (-not (Test-Path $exe)) { throw "AIThread.exe tidak ditemukan di $Output." }

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Selesai: $exe ($sizeMb MB)" -ForegroundColor Green

if ($Zip) {
    $zipPath = Join-Path (Split-Path -Parent $Output) 'AIThread-win-x64.zip'
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $Output '*') -DestinationPath $zipPath
    Write-Host "Zip: $zipPath" -ForegroundColor Green
}

Write-Host 'Jalankan: dist\AIThread\AIThread.exe   (menu)   |   AIThread.exe help   (semua perintah)'
