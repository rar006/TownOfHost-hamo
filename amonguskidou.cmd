@echo off
setlocal EnableExtensions
chcp 65001 >nul

rem ============================================================
rem  TownOfHost-hamo 配信スタート
rem  1. Among Usの外でWatchdogを起動する
rem  2. Among Usを起動する
rem  3. 起動直後のクラッシュもWatchdogが検知して再起動する
rem ============================================================

set "SCRIPT_DIR=%~dp0"
set "WATCHDOG=%SCRIPT_DIR%TOHhamoWatchdog.ps1"
set "LOG_DIR=%USERPROFILE%\Desktop\TOHhamo_Logs"
set "STOP_FLAG=%LOG_DIR%\watchdog-stop.flag"

if not exist "%WATCHDOG%" (
    echo [ERROR] TOHhamoWatchdog.ps1 が見つかりません。
    echo この.cmdとTOHhamoWatchdog.ps1を同じフォルダに置いてください。
    pause
    exit /b 1
)

if not exist "%LOG_DIR%" mkdir "%LOG_DIR%" >nul 2>&1
if exist "%STOP_FLAG%" del /f /q "%STOP_FLAG%" >nul 2>&1

echo [1/2] Watchdogを起動しています...
start "TOHhamo Watchdog" /min powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Minimized -File "%WATCHDOG%"
timeout /t 2 /nobreak >nul

echo [2/2] Among Usを起動しています...
set "AU_EXE="

rem Steam版の標準インストール先
if exist "C:\Program Files (x86)\Steam\steamapps\common\Among Us\Among Us.exe" set "AU_EXE=C:\Program Files (x86)\Steam\steamapps\common\Among Us\Among Us.exe"
if not defined AU_EXE if exist "C:\Program Files\Steam\steamapps\common\Among Us\Among Us.exe" set "AU_EXE=C:\Program Files\Steam\steamapps\common\Among Us\Among Us.exe"

rem Epic版の標準インストール先
if not defined AU_EXE if exist "C:\Program Files\Epic Games\AmongUs\Among Us.exe" set "AU_EXE=C:\Program Files\Epic Games\AmongUs\Among Us.exe"

if defined AU_EXE (
    start "Among Us" "%AU_EXE%"
    echo Among Usを起動しました。
) else (
    rem 独自のSteamライブラリ等でパスが見つからない場合はSteamのゲームIDで起動を試みる。
    start "Among Us (Steam)" "steam://rungameid/945360"
    echo 標準パスが見つからなかったため、Steam経由で起動を試みました。
)

echo.
echo Watchdogはバックグラウンドで動作しています。
echo 正常にAmong Usを終了した場合は、MODがWatchdogへ停止通知を送ります。
timeout /t 3 /nobreak >nul
exit /b 0
