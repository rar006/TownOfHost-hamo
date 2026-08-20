<#
 TownOfHost-hamo Watchdog
 Among Us のクラッシュ／ハングをゲーム外で検知し、再起動後にMODのAutoRehostへ
 ロビー再作成を依頼する。MODが停止した後も動作するため、PowerShellと
 タスクスケジューラから起動されることを前提とする。
#>

# 予期しない例外を握りつぶさず、watchdog_log.txtへ残して終了コードを返す。
$ErrorActionPreference = 'Stop'
$MutexName = 'Local\TOHhamoWatchdog'
$ProcessName = 'Among Us'
$HealthDir = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'TOHhamo_Logs'
$HealthLogPath = Join-Path $HealthDir 'TOHhamo-Health.log'
$StopFlagPath = Join-Path $HealthDir 'watchdog-stop.flag'
$RelaunchMarkerPath = Join-Path $HealthDir 'restart_request.flag'
$LastExePath = Join-Path $HealthDir 'last-among-us-exe.txt'
$WatchLogPath = Join-Path $HealthDir 'watchdog_log.txt'

$CheckIntervalSeconds = 15
$StaleHeartbeatSeconds = 90
$BootGraceSeconds = 120
$RelaunchCooldownSeconds = 90
$MaxRelaunchPerHour = 12

$mutex = New-Object System.Threading.Mutex($false, $MutexName, [ref]$createdNew)
if (-not $createdNew) { exit 0 }

$script:LastRelaunch = [datetime]::MinValue
$script:GraceUntil = (Get-Date).AddSeconds($BootGraceSeconds)
$script:RelaunchTimes = New-Object System.Collections.Generic.List[datetime]

function Write-WatchLog {
    param([string]$Message)
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $Message"
    try { Add-Content -Path $WatchLogPath -Value $line -Encoding UTF8 } catch { }
}

function Get-AmongUsProcess {
    Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
}

function Save-ExecutablePath {
    param($Process)
    try {
        if ($Process -and $Process.Path -and (Test-Path $Process.Path)) {
            Set-Content -Path $LastExePath -Value $Process.Path -Encoding UTF8
        }
    } catch { }
}

function Resolve-AmongUsExePath {
    try {
        if (Test-Path $LastExePath) {
            $saved = (Get-Content $LastExePath -TotalCount 1).Trim()
            if ($saved -and (Test-Path $saved)) { return $saved }
        }
    } catch { }

    $candidates = @(
        'C:\Program Files (x86)\Steam\steamapps\common\Among Us\Among Us.exe',
        'C:\Program Files\Steam\steamapps\common\Among Us\Among Us.exe',
        'C:\Program Files\Epic Games\AmongUs\Among Us.exe'
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

function Get-HeartbeatAgeSeconds {
    if (-not (Test-Path $HealthLogPath)) { return [double]::PositiveInfinity }
    try { return ((Get-Date) - (Get-Item $HealthLogPath).LastWriteTime).TotalSeconds }
    catch { return [double]::PositiveInfinity }
}

function Test-RelaunchAllowed {
    $cutoff = (Get-Date).AddHours(-1)
    for ($i = $script:RelaunchTimes.Count - 1; $i -ge 0; $i--) {
        if ($script:RelaunchTimes[$i] -lt $cutoff) { $script:RelaunchTimes.RemoveAt($i) }
    }
    return $script:RelaunchTimes.Count -lt $MaxRelaunchPerHour
}

function Start-AmongUs {
    $exe = Resolve-AmongUsExePath
    if (-not $exe) {
        Write-WatchLog 'Among Us.exe を見つけられません。ゲームを一度手動起動してから再試行してください。'
        return $false
    }

    try {
        New-Item -ItemType Directory -Path $HealthDir -Force | Out-Null
        Set-Content -Path $RelaunchMarkerPath -Value (Get-Date -Format 'o') -Encoding UTF8
        Start-Process -FilePath $exe | Out-Null
        $script:LastRelaunch = Get-Date
        $script:GraceUntil = (Get-Date).AddSeconds($BootGraceSeconds)
        $script:RelaunchTimes.Add((Get-Date))
        Write-WatchLog "Among Usを再起動しました: $exe"
        return $true
    } catch {
        Write-WatchLog "Among Usの再起動に失敗しました: $($_.Exception.Message)"
        return $false
    }
}

function Stop-FrozenAmongUs {
    try {
        Stop-Process -Name $ProcessName -Force -ErrorAction SilentlyContinue
        Write-WatchLog '心拍停止を検知したためAmong Usを強制終了しました。'
        Start-Sleep -Seconds 3
    } catch { }
}

try {
    New-Item -ItemType Directory -Path $HealthDir -Force | Out-Null
    Write-WatchLog 'TownOfHost-hamo Watchdogを開始しました。'

    while ($true) {
        Start-Sleep -Seconds $CheckIntervalSeconds
        if (Test-Path $StopFlagPath) {
            Remove-Item $StopFlagPath -Force -ErrorAction SilentlyContinue
            Write-WatchLog 'MODから通常終了の通知を受け取ったためWatchdogを停止します。'
            break
        }

        $process = Get-AmongUsProcess
        if ($process) { Save-ExecutablePath $process }
        $now = Get-Date
        if ($now -lt $script:GraceUntil) { continue }
        if ((($now - $script:LastRelaunch).TotalSeconds) -lt $RelaunchCooldownSeconds) { continue }

        $needsRestart = $false
        if (-not $process) {
            Write-WatchLog 'Among Usプロセスの消失を検知しました。'
            $needsRestart = $true
        } else {
            $heartbeatAge = Get-HeartbeatAgeSeconds
            if ($heartbeatAge -gt $StaleHeartbeatSeconds) {
                Write-WatchLog "心拍が$([int]$heartbeatAge)秒停止しました。フリーズとして再起動します。"
                Stop-FrozenAmongUs
                $needsRestart = $true
            }
        }

        if ($needsRestart) {
            if (Test-RelaunchAllowed) { Start-AmongUs | Out-Null }
            else { Write-WatchLog '1時間あたりの再起動上限に達したため再起動を保留します。' }
        }
    }
}
catch {
    # 起動直後の構文・権限・パス問題もログで確認できるようにする。
    Write-WatchLog "Watchdogの予期しないエラー: $($_.Exception.Message)"
    exit 1
}
finally {
    try { $mutex.ReleaseMutex() } catch { }
    try { $mutex.Dispose() } catch { }
}
