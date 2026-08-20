<#
============================================================
 TOHhamoWatchdog.ps1
 From: TownOfHost_hamo (外部ウォッチドッグ / MOD本体とは別ファイル)
 参考: Aeterna-End-K-not の EndKnotWatchdog.ps1 の設計を、
       TOHhamo向けに簡略化して移植したもの。
------------------------------------------------------------
 概要:
   Among Us (TownOfHost-hamo導入環境) の「クラッシュ」だけでなく
   「フリーズ(ハング)」も検知して自動的に再起動する外部監視スクリプト。

   仕組み:
   ・TOHhamo の HealthLog モジュールが
     <デスクトップ>\TOHhamo_Logs\TOHhamo-Health.log に
     5秒ごとに心拍(HB行)を書き込んでいる(MOD側で "Auto Restart On Crash" をON時)。
   ・この番犬はそのファイルの最終更新時刻を見張り、途切れたら異常と判断する:
       - Among Usプロセスが消えていれば → クラッシュ/終了 → 起動し直す
       - プロセスは生きているのに心拍が止まっていれば → フリーズ → 強制終了して起動し直す
   ・起動後は TOHhamo の AutoRehost 機能(MOD内)が同じ設定で部屋を立て直す。

 使い方:
   1. 事前にMOD側の設定で "Auto Restart On Crash" をONにしておく
      (BepInEx/config/TownOfHost.TOHhamo.cfg 内の該当項目)
   2. このファイルの [設定] セクションを自分の環境に合わせて書き換える
   3. PowerShellで実行しっぱなしにする:
        powershell -ExecutionPolicy Bypass -File ".\TOHhamoWatchdog.ps1"
   4. 監視を止めたい時は、そのウィンドウで Ctrl + C

 注意:
   ・MOD本体(C#/BepInEx側)には一切手を加えない、完全に独立した外部プログラムです。
   ・心拍ログが無い(MOD側の設定がOFF)場合は、プロセス生死だけを見て再起動します。
============================================================
#>

param(
    [int]$RunSeconds = 0   # 0 = 無限(通常運用)。>0でその秒数後に自動終了(テスト用)。
)

# ===================== [設定] =====================

# Among Us の実行ファイルパス(Steam版の例)
$AmongUsExePath = "C:\Program Files (x86)\Steam\steamapps\common\Among Us\Among Us.exe"

# Epic版を使う場合はこちらを指定(Steam版と両方設定した場合、存在する方を優先的に使う)
# 例: "C:\Program Files\Epic Games\AmongUs\Among Us.exe"
$AmongUsExePathEpic = ""

# プロセス名(拡張子なし)。通常は変更不要
$ProcessName = "Among Us"

# TOHhamoのHealthLogの出力先(MOD側と同じ算出方法。通常このままでOK)
$HealthDir = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'TOHhamo_Logs'
$HealthLogPath = Join-Path $HealthDir 'TOHhamo-Health.log'

# 心拍(5秒毎)がこの秒数途切れたら「フリーズ/クラッシュ」とみなす
$StaleSeconds = 90

# 番犬の巡回間隔(秒)
$CheckIntervalSec = 15

# (再)起動直後、この秒数は監視を猶予する(起動→ホスト→心拍開始まで待つ)
$BootGraceSec = 120

# 連続再起動の最短間隔(秒)。短時間の二重発火を防ぐ
$RelaunchCooldownSec = 90

# 1時間あたりの再起動上限。超えたら暴走とみなして一時停止する
$MaxRelaunchPerHour = 12

# 回線死活プローブ: 再起動前に回線が生きているかをTCP 443接続で確認する
# (回線が死んでいる間に再起動を繰り返しても意味がないため)
$NetProbeEnabled = $true
$NetProbeHosts = @('1.1.1.1', '8.8.8.8')

# Among Usが最初から起動していない場合に番犬が起動するか
$LaunchIfNotRunning = $true

# ログファイルの出力先(このスクリプトと同じフォルダに作成)
$WatchLogPath = Join-Path $PSScriptRoot "watchdog_log.txt"

# ===================================================

$script:LastRelaunch = [datetime]::MinValue
$script:GraceUntil = [datetime]::MinValue
$script:RelaunchTimes = New-Object System.Collections.Generic.List[datetime]

function Write-WatchLog {
    param([string]$Message, [string]$Color = 'White')
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $line = "[$timestamp] $Message"
    Write-Host $line -ForegroundColor $Color
    try { Add-Content -Path $WatchLogPath -Value $line } catch { }
}

# 番犬が再起動した時、MOD側に「自動で部屋を立てて」と伝えるマーカーファイルを置くか
$AutoHostOnRelaunch = $true
$RelaunchMarkerPath = Join-Path $HealthDir 'restart_request.flag'

function Resolve-AmongUsExePath {
    if ($AmongUsExePathEpic -and (Test-Path $AmongUsExePathEpic)) { return $AmongUsExePathEpic }
    if (Test-Path $AmongUsExePath) { return $AmongUsExePath }
    return $null
}

function Get-AmongUsProcess {
    Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
}

function Get-HeartbeatStatus {
    $result = @{ Exists = $false; Fresh = $false; AgeSec = [double]::PositiveInfinity; LastLine = $null }
    if (-not (Test-Path $HealthLogPath)) { return $result }
    $result.Exists = $true
    try {
        $lastWrite = (Get-Item $HealthLogPath).LastWriteTime
        $result.AgeSec = ((Get-Date) - $lastWrite).TotalSeconds
        $result.Fresh = $result.AgeSec -le $StaleSeconds
        $result.LastLine = Get-Content $HealthLogPath -Tail 1 -ErrorAction SilentlyContinue
    } catch { }
    return $result
}

function Test-InternetAlive {
    if (-not $NetProbeEnabled) { return $true }
    foreach ($h in $NetProbeHosts) {
        $c = $null
        try {
            $c = New-Object System.Net.Sockets.TcpClient
            $ar = $c.BeginConnect($h, 443, $null, $null)
            if ($ar.AsyncWaitHandle.WaitOne(3000) -and $c.Connected) { $c.Close(); return $true }
        } catch { }
        finally { if ($c) { try { $c.Close() } catch { } } }
    }
    return $false
}

function Test-RelaunchAllowed {
    $cutoff = (Get-Date).AddHours(-1)
    for ($i = $script:RelaunchTimes.Count - 1; $i -ge 0; $i--) {
        if ($script:RelaunchTimes[$i] -lt $cutoff) { $script:RelaunchTimes.RemoveAt($i) }
    }
    return ($script:RelaunchTimes.Count -lt $MaxRelaunchPerHour)
}

function Stop-Au {
    try {
        Stop-Process -Name $ProcessName -Force -ErrorAction SilentlyContinue
        Write-WatchLog "フリーズしたAmong Usを強制終了しました。" 'Yellow'
        Start-Sleep -Seconds 3
    } catch {
        Write-WatchLog "強制終了に失敗: $($_.Exception.Message)" 'Red'
    }
}

function Start-Au {
    $exe = Resolve-AmongUsExePath
    if (-not $exe) {
        Write-WatchLog "エラー: Among Usの実行ファイルが見つかりません。設定内のパスを確認してください。" 'Red'
        return $false
    }
    try {
        if ($AutoHostOnRelaunch) {
            try {
                if (-not (Test-Path $HealthDir)) { New-Item -ItemType Directory -Path $HealthDir -Force | Out-Null }
                Set-Content -Path $RelaunchMarkerPath -Value (Get-Date -Format 'o')
                Write-WatchLog "再起動マーカーを作成しました(起動後にMOD側が自動で部屋を立てます)。" 'DarkGray'
            } catch {
                Write-WatchLog "再起動マーカーの作成に失敗: $($_.Exception.Message)" 'Yellow'
            }
        }
        Start-Process -FilePath $exe | Out-Null
        Write-WatchLog "Among Usを起動しました。($exe)" 'Cyan'
        $script:LastRelaunch = Get-Date
        $script:GraceUntil = (Get-Date).AddSeconds($BootGraceSec)
        $script:RelaunchTimes.Add((Get-Date))
        return $true
    } catch {
        Write-WatchLog "起動に失敗: $($_.Exception.Message)" 'Red'
        return $false
    }
}

# ===================== メイン処理 =====================

Write-WatchLog "====================================================" 'Cyan'
Write-WatchLog "TOHhamo ウォッチドッグを開始します。(From: TownOfHost_hamo)" 'Cyan'
Write-WatchLog "心拍ログ監視先: $HealthLogPath" 'Cyan'
Write-WatchLog "確認間隔: ${CheckIntervalSec}秒 / 心拍猶予: ${StaleSeconds}秒" 'Cyan'
Write-WatchLog "====================================================" 'Cyan'

$startTime = Get-Date

$proc = Get-AmongUsProcess
if (-not $proc -and $LaunchIfNotRunning) {
    Start-Au | Out-Null
} elseif ($proc) {
    Write-WatchLog "既存のAmong Usプロセスを検出しました。(PID: $($proc.Id)) 監視を開始します。" 'Green'
    # 既に起動済みの場合も、心拍がまだ無ければ猶予期間を与える
    $script:GraceUntil = (Get-Date).AddSeconds($BootGraceSec)
}

while ($true) {
    Start-Sleep -Seconds $CheckIntervalSec

    if ($RunSeconds -gt 0 -and ((Get-Date) - $startTime).TotalSeconds -ge $RunSeconds) {
        Write-WatchLog "指定時間が経過したため監視を終了します。" 'Cyan'
        break
    }

    $now = Get-Date
    $proc = Get-AmongUsProcess
    $health = Get-HeartbeatStatus
    $inGrace = $now -lt $script:GraceUntil

    # --- 正常: プロセス生存 かつ (心拍が新鮮 or 心拍ログ自体を使っていない) ---
    if ($proc -and ($health.Fresh -or -not $health.Exists)) {
        $ageStr = if ($health.Exists) { "{0:N0}s前" -f $health.AgeSec } else { "(心拍ログ未使用)" }
        Write-WatchLog "OK  proc=生存 心拍=$ageStr" 'Green'
        continue
    }

    # --- 起動猶予中はどんな状態でも待つ ---
    if ($inGrace) {
        $left = [int]($script:GraceUntil - $now).TotalSeconds
        $ageStr = if ($health.Exists) { "$([int]$health.AgeSec)s" } else { 'なし' }
        Write-WatchLog "起動猶予中... 残り${left}s (proc=$([bool]$proc) 心拍鮮度=$ageStr)" 'DarkGray'
        continue
    }

    # --- クールダウン中は待つ ---
    $sinceRelaunch = ($now - $script:LastRelaunch).TotalSeconds
    if ($sinceRelaunch -lt $RelaunchCooldownSec) {
        Write-WatchLog ("再起動クールダウン中... 経過 {0:N0}s / {1}s" -f $sinceRelaunch, $RelaunchCooldownSec) 'DarkGray'
        continue
    }

    # --- 異常判定 ---
    if (-not $proc) {
        Write-WatchLog "異常: Among Usのプロセスが見つかりません(クラッシュまたは終了)。" 'Red'
    } elseif (-not $health.Fresh -and $health.Exists) {
        Write-WatchLog ("異常: プロセスは生存していますが心拍が {0:N0}秒 途切れています(フリーズの疑い)。" -f $health.AgeSec) 'Red'
        Stop-Au
    } else {
        continue
    }

    if (-not (Test-RelaunchAllowed)) {
        Write-WatchLog "直近1時間の再起動回数が上限(${MaxRelaunchPerHour}回)に達しました。暴走防止のため保留します。" 'Magenta'
        continue
    }

    if (-not (Test-InternetAlive)) {
        Write-WatchLog "回線が生きていないため、再起動を保留して回線の回復を待ちます。" 'Magenta'
        continue
    }

    Start-Au | Out-Null
}

Write-WatchLog "ウォッチドッグを終了しました。" 'Cyan'
