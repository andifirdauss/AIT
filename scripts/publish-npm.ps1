<#
.SYNOPSIS
    Membuat paket npm (aithread + aithread-win-x64) di dist\npm.

.DESCRIPTION
    Memanggil publish-exe.ps1 ke npm\platforms\win-x64\bin, menyamakan versi paket dengan <Version> di AiThread.UI.csproj,
    lalu menjalankan `npm pack`. Paket utama (aithread) hanya berisi launcher; AiThread.exe ada di aithread-win-x64
    yang ikut terpasang lewat optionalDependencies.

.EXAMPLE
    .\scripts\publish-npm.ps1                 # hanya membuat .tgz di dist\npm
    .\scripts\publish-npm.ps1 -Publish        # lalu npm publish (platform dulu, baru launcher)
#>
[CmdletBinding()]
param(
    # Jalankan `npm publish` untuk kedua paket setelah dibuat.
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Csproj = Join-Path $RepoRoot 'src\AiThread.UI\AiThread.UI.csproj'
$Main = Join-Path $RepoRoot 'npm\aithread'
$Platform = Join-Path $RepoRoot 'npm\platforms\win-x64'
$Out = Join-Path $RepoRoot 'dist\npm'

if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { throw 'npm tidak ditemukan di PATH.' }

$version = ([xml](Get-Content $Csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "<Version> tidak ditemukan di $Csproj." }

function Set-PackageJson([string]$Dir, [scriptblock]$Edit) {
    $file = Join-Path $Dir 'package.json'
    $json = Get-Content $file -Raw | ConvertFrom-Json
    & $Edit $json
    # BOM-less UTF-8: npm rejects a BOM in package.json.
    [IO.File]::WriteAllText($file, (($json | ConvertTo-Json -Depth 10) + "`n"), (New-Object Text.UTF8Encoding($false)))
}

Set-PackageJson $Platform { param($j) $j.version = $version }
Set-PackageJson $Main { param($j) $j.version = $version; $j.optionalDependencies.'aithread-win-x64' = $version }

& (Join-Path $PSScriptRoot 'publish-exe.ps1') -OutputDir (Join-Path $Platform 'bin')

# npm bundles a LICENSE file found in the package folder; the copies are ignored by git.
foreach ($dir in $Platform, $Main) { Copy-Item (Join-Path $RepoRoot 'LICENSE') $dir -Force }

if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Path $Out | Out-Null

foreach ($dir in $Platform, $Main) {
    Push-Location $dir
    try {
        & npm pack --pack-destination $Out --silent
        if ($LASTEXITCODE -ne 0) { throw "npm pack gagal di $dir." }
        if ($Publish) {
            & npm publish --access public
            if ($LASTEXITCODE -ne 0) { throw "npm publish gagal di $dir." }
        }
    }
    finally { Pop-Location }
}

Get-ChildItem $Out -Filter *.tgz | ForEach-Object { Write-Host ("{0}  {1} MB" -f $_.Name, [math]::Round($_.Length / 1MB, 1)) -ForegroundColor Green }
Write-Host "Coba: npm i -g (Get-ChildItem $Out\aithread-win-x64-*.tgz) (Get-ChildItem $Out\aithread-$version.tgz)"
