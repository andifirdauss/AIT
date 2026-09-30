<#
.SYNOPSIS
    Menjalankan AiThread di background: tetap hidup walau browser, terminal, atau Visual Studio ditutup.

.DESCRIPTION
    Perintah:
      start          Publish (Release) ke %LOCALAPPDATA%\AiThread\app lalu jalankan tanpa jendela.
      stop           Matikan dengan rapi; thread yang sedang berjalan dibatalkan dan dicatat.
      restart        stop, publish ulang, lalu start. Pakai setelah kode berubah.
      status         Instance yang berjalan, lokasi log, dan status autostart.
      logs           Log terbaru (-Follow untuk terus mengikuti, Ctrl+C untuk keluar).
      autostart-on   Jalankan otomatis saat login Windows (Task Scheduler, tanpa hak admin).
      autostart-off  Hapus autostart.

.EXAMPLE
    .\scripts\aithread.ps1 start -Open

.EXAMPLE
    .\scripts\aithread.ps1 logs -Follow
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('start', 'stop', 'restart', 'status', 'logs', 'autostart-on', 'autostart-off')]
    [string]$Command = 'status',

    # Harus sama dengan ANTHROPIC_BASE_URL di integrasi Claude Code.
    [int]$Port = 5225,

    # start: pakai hasil publish terakhir tanpa build ulang.
    [switch]$NoBuild,

    # start/restart: buka dashboard di browser setelah siap.
    [switch]$Open,

    # logs: terus ikuti log baru.
    [switch]$Follow,

    # logs: jumlah baris terakhir yang ditampilkan.
    [int]$Tail = 80
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectFile = Join-Path $RepoRoot 'src\AiThread.UI\AiThread.UI.csproj'
$HomeDir = Join-Path $env:LOCALAPPDATA 'AiThread'
$AppDir = Join-Path $HomeDir 'app'
$BuildDir = Join-Path $HomeDir 'build'
$LogDir = Join-Path $HomeDir 'logs'
$PidFile = Join-Path $HomeDir 'aithread.pid'
$ExePath = Join-Path $AppDir 'AiThread.UI.exe'
$TaskName = 'AiThread'
$BaseUrl = "http://localhost:$Port"
$KeepLogRuns = 10
$StartTimeoutSeconds = 120
$StopTimeoutMs = 45000

function Write-Ok([string]$Message) { Write-Host $Message -ForegroundColor Green }
function Write-Note([string]$Message) { Write-Host $Message -ForegroundColor Yellow }

# The AiThread answering on the port (background, Visual Studio, or dotnet run), or $null.
function Get-RunningInstance {
    try {
        $response = Invoke-RestMethod -Uri "$BaseUrl/api/system" -TimeoutSec 3 -UseBasicParsing
        if ($response.success) { return $response.data }
    }
    catch { }
    return $null
}

function Test-IsBackgroundInstance($Instance) {
    return $Instance.contentRoot.TrimEnd('\') -ieq $AppDir.TrimEnd('\')
}

# The process recorded by 'start', while it is still alive.
function Get-BackgroundProcess {
    if (-not (Test-Path $PidFile)) { return $null }
    try {
        $process = Get-Process -Id ([int](Get-Content $PidFile -Raw).Trim()) -ErrorAction SilentlyContinue
        if ($process -and $process.ProcessName -eq 'AiThread.UI') { return $process }
    }
    catch { }
    return $null
}

function Get-PortOwner {
    $listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $listener) { return $null }
    $process = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
    $name = if ($process) { $process.ProcessName } else { '?' }
    return "PID $($listener.OwningProcess), $name"
}

function Get-LatestLog {
    return Get-ChildItem $LogDir -Filter 'aithread-*.log' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike '*.err.log' } |
        Sort-Object Name -Descending |
        Select-Object -First 1
}

function Get-ErrorLogPath([string]$LogPath) {
    return $LogPath -replace '\.log$', '.err.log'
}

function Remove-OldLogs {
    Get-ChildItem $LogDir -Filter 'aithread-*.log' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike '*.err.log' } |
        Sort-Object Name -Descending |
        Select-Object -Skip ($KeepLogRuns - 1) |
        ForEach-Object {
            Remove-Item $_.FullName -ErrorAction SilentlyContinue
            Remove-Item (Get-ErrorLogPath $_.FullName) -ErrorAction SilentlyContinue
        }
}

function Show-LogTail([string]$Path, [int]$Lines) {
    if ((Test-Path $Path) -and (Get-Item $Path).Length -gt 0) {
        Write-Host "---- $Path" -ForegroundColor DarkGray
        Get-Content $Path -Tail $Lines
    }
}

# Publishes to a staging folder first, so a failed build leaves the previous version intact. Builds in its own
# artifacts folder: bin/obj in the repo may be locked by an instance running from Visual Studio or dotnet run.
function Publish-App {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK (dotnet) tidak ditemukan di PATH.' }

    $staging = "$AppDir.new"
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }

    Write-Host "Publish AiThread (Release) ke $AppDir ..."
    & dotnet publish $ProjectFile -c Release -o $staging --artifacts-path $BuildDir --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish gagal (exit $LASTEXITCODE). Versi yang sudah ter-publish tidak diubah." }

    if (Test-Path $AppDir) { Remove-Item $AppDir -Recurse -Force }
    Move-Item $staging $AppDir
}

function Start-App {
    $instance = Get-RunningInstance
    if ($instance) {
        Write-Ok "AiThread sudah berjalan di $BaseUrl (PID $($instance.processId))."
        if (-not (Test-IsBackgroundInstance $instance)) {
            Write-Note 'Instance ini bukan dari script ini (mis. Visual Studio / dotnet run) dan ikut berhenti bila program itu ditutup.'
            Write-Note "Hentikan dulu, lalu jalankan: .\scripts\aithread.ps1 start"
        }
        return
    }

    $owner = Get-PortOwner
    if ($owner) { throw "Port $Port sudah dipakai proses lain ($owner). Tutup proses itu atau pakai -Port lain." }

    $previous = Get-BackgroundProcess
    if ($previous) { throw "AiThread background (PID $($previous.Id)) masih hidup tetapi tidak menjawab di $BaseUrl. Jalankan 'stop' dulu." }

    if (-not $NoBuild -or -not (Test-Path $ExePath)) { Publish-App }

    New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
    Remove-OldLogs
    $log = Join-Path $LogDir ("aithread-{0}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $errorLog = Get-ErrorLogPath $log

    # Inherited by the child process.
    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    $env:AiThread__DocsPath = Join-Path $AppDir 'docs'

    $process = Start-Process -FilePath $ExePath -ArgumentList '--urls', $BaseUrl -WorkingDirectory $AppDir `
        -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError $errorLog -PassThru
    $null = $process.Handle # keeps ExitCode readable after the process exits
    Set-Content -Path $PidFile -Value $process.Id -Encoding Ascii

    Write-Host "Menunggu AiThread siap (PID $($process.Id)) ..."
    $deadline = (Get-Date).AddSeconds($StartTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) {
            Show-LogTail $log 30
            Show-LogTail $errorLog 30
            Remove-Item $PidFile -ErrorAction SilentlyContinue
            throw "AiThread berhenti saat start (exit $($process.ExitCode)). Log: $log"
        }
        if (Get-RunningInstance) {
            Write-Ok "AiThread berjalan di background: $BaseUrl (PID $($process.Id))"
            Write-Host 'Browser, terminal, dan Visual Studio sekarang boleh ditutup.'
            Write-Host "Log: $log"
            if ($Open) { Start-Process $BaseUrl }
            return
        }
        Start-Sleep -Milliseconds 500
    }
    Write-Note "AiThread belum menjawab setelah $StartTimeoutSeconds detik; prosesnya tetap berjalan. Periksa: .\scripts\aithread.ps1 logs"
}

function Stop-App {
    $instance = Get-RunningInstance
    $process = $null
    if ($instance) { $process = Get-Process -Id $instance.processId -ErrorAction SilentlyContinue }
    if (-not $process) { $process = Get-BackgroundProcess }
    if (-not $process) {
        Remove-Item $PidFile -ErrorAction SilentlyContinue
        Write-Host "AiThread tidak berjalan di $BaseUrl."
        return
    }

    Write-Host "Mematikan AiThread (PID $($process.Id)) ..."
    $graceful = $false
    if ($instance) {
        try {
            Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/system/shutdown" -Headers @{ 'X-AiThread-Shutdown' = '1' } -TimeoutSec 10 -UseBasicParsing | Out-Null
            $graceful = $true
        }
        catch { Write-Note "Permintaan shutdown gagal: $($_.Exception.Message)" }
    }

    if (-not $graceful -or -not $process.WaitForExit($StopTimeoutMs)) {
        Write-Note 'AiThread tidak berhenti dengan rapi; proses dihentikan paksa.'
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        $null = $process.WaitForExit(10000)
    }

    Remove-Item $PidFile -ErrorAction SilentlyContinue
    Write-Ok 'AiThread berhenti.'
}

function Show-Status {
    $instance = Get-RunningInstance
    if ($instance) {
        $mode = if (Test-IsBackgroundInstance $instance) { 'background (script ini)' } else { 'bukan background - ikut berhenti bila Visual Studio / terminalnya ditutup' }
        Write-Ok "AiThread berjalan di $BaseUrl"
        Write-Host "  PID       : $($instance.processId)"
        Write-Host "  Mode      : $mode"
        Write-Host ("  Sejak     : {0:yyyy-MM-dd HH:mm:ss}" -f ([datetime]$instance.startedAt).ToLocalTime())
        Write-Host "  Folder    : $($instance.contentRoot)"
    }
    else {
        $process = Get-BackgroundProcess
        if ($process) { Write-Note "Proses AiThread background (PID $($process.Id)) hidup tetapi tidak menjawab di $BaseUrl." }
        else { Write-Host "AiThread tidak berjalan di $BaseUrl." }
    }

    $log = Get-LatestLog
    if ($log) { Write-Host "  Log       : $($log.FullName)" }
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    $autostart = if ($task) { 'aktif (saat login Windows)' } else { 'tidak aktif' }
    Write-Host "  Autostart : $autostart"
}

function Show-Logs {
    $log = Get-LatestLog
    if (-not $log) {
        Write-Host "Belum ada log di $LogDir."
        return
    }

    Show-LogTail (Get-ErrorLogPath $log.FullName) $Tail
    Write-Host "---- $($log.FullName)" -ForegroundColor DarkGray
    if ($Follow) { Get-Content $log.FullName -Tail $Tail -Wait }
    else { Get-Content $log.FullName -Tail $Tail }
}

# Runs as the current user (no admin, no stored password), so LocalDB, the encrypted 9router key,
# and the Claude Code CLI login keep working exactly as in development.
function Enable-Autostart {
    $user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$PSCommandPath`" start -NoBuild -Port $Port"
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments -WorkingDirectory $RepoRoot
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
    $trigger.Delay = 'PT30S' # give LocalDB a head start
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
        -ExecutionTimeLimit (New-TimeSpan -Minutes 10)
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited

    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal `
        -Description 'Menjalankan AiThread di background saat login (scripts\aithread.ps1).' -Force | Out-Null

    Write-Ok "Autostart aktif: AiThread dijalankan 30 detik setelah $user login (port $Port)."
    Write-Host 'Autostart memakai hasil publish terakhir; setelah kode berubah jalankan: .\scripts\aithread.ps1 restart'
    if (-not (Test-Path $ExePath)) { Write-Note 'Belum ada hasil publish; login berikutnya akan mem-publish dulu (lebih lama).' }
}

function Disable-Autostart {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Ok 'Autostart dihapus.'
    }
    else {
        Write-Host 'Autostart memang tidak aktif.'
    }
}

try {
    switch ($Command) {
        'start' { Start-App }
        'stop' { Stop-App }
        'restart' { Stop-App; Start-App }
        'status' { Show-Status }
        'logs' { Show-Logs }
        'autostart-on' { Enable-Autostart }
        'autostart-off' { Disable-Autostart }
    }
}
catch {
    Write-Host "Gagal: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
