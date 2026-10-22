@echo off
chcp 65001 > nul
setlocal
rem ダブルクリックで、ノーマル版(Release)とβ版(Debug)の両方をビルドして dist に入れる
rem   dist\Normal\TownOfHost-hamo.dll  … ノーマル版(正式リリース用)
rem   dist\Beta\TownOfHost-hamo.dll    … β版(デバッグ版。デバッグ用の枠・試合中のBUG報告などが使える)
rem 最後に、どちらを Among Us の BepInEx\plugins に入れるか選べる
rem (注意: if の括弧の中に括弧を含む文字を書くと cmd が読み違えるので、goto で分けて書いている)
cd /d "%~dp0"

echo ==== ノーマル版(Release)をビルドしています ====
dotnet build TownOfHost.csproj -c Release -p:CopyToAmongUs=false -o dist\Normal -v q -nologo
if errorlevel 1 goto :failed

echo.
echo ==== β版(Debug)をビルドしています ====
dotnet build TownOfHost.csproj -c Debug -p:CopyToAmongUs=false -o dist\Beta -v q -nologo
if errorlevel 1 goto :failed

echo.
echo ==== ビルドが完了しました ====
echo ノーマル版: %~dp0dist\Normal\TownOfHost-hamo.dll
echo β版      : %~dp0dist\Beta\TownOfHost-hamo.dll

rem ===== Among Us のフォルダを探す(TownOfHost.csproj と同じ順番) =====
set "AU="
if defined AMONGUS_PATH set "AU=%AMONGUS_PATH%"
if not defined AU if exist "C:\Program Files (x86)\Steam\steamapps\common\Among Us\BepInEx\plugins" set "AU=C:\Program Files (x86)\Steam\steamapps\common\Among Us"
if not defined AU if exist "C:\Program Files\Steam\steamapps\common\Among Us\BepInEx\plugins" set "AU=C:\Program Files\Steam\steamapps\common\Among Us"
if not defined AU if exist "D:\SteamLibrary\steamapps\common\Among Us\BepInEx\plugins" set "AU=D:\SteamLibrary\steamapps\common\Among Us"
if not defined AU if exist "D:\Steam\steamapps\common\Among Us\BepInEx\plugins" set "AU=D:\Steam\steamapps\common\Among Us"
if not defined AU if exist "E:\SteamLibrary\steamapps\common\Among Us\BepInEx\plugins" set "AU=E:\SteamLibrary\steamapps\common\Among Us"
if not defined AU if exist "C:\Program Files\Epic Games\AmongUs\BepInEx\plugins" set "AU=C:\Program Files\Epic Games\AmongUs"
if not defined AU goto :noau

echo.
echo Among Us: %AU%
echo どちらをゲームに入れますか？  1: ノーマル版   2: β版   0: 入れない
choice /c 120 /n /m "番号を押してください: "
if errorlevel 3 goto :skip
set "SRC=dist\Normal"
set "KIND=ノーマル版"
if errorlevel 2 set "SRC=dist\Beta"
if errorlevel 2 set "KIND=β版"

:copy
copy /y "%SRC%\TownOfHost-hamo.dll" "%AU%\BepInEx\plugins\TownOfHost-hamo.dll" > nul
if not errorlevel 1 goto :copied
echo.
echo 入れ替えに失敗しました。Among Us を起動していると入れ替えられません。
echo Among Us を閉じてから何かキーを押すと、もう一度試します。やめる時はこの画面を閉じてください。
pause > nul
goto :copy

:copied
echo.
echo %KIND% を Among Us に入れました: %AU%\BepInEx\plugins\TownOfHost-hamo.dll
pause
exit /b 0

:noau
echo.
echo Among Us のフォルダが見つからなかったので、ゲームには入れていません。
echo 環境変数 AMONGUS_PATH に Among Us のフォルダを入れると、そこに入れられます。
pause
exit /b 0

:skip
echo ゲームには入れませんでした。
pause
exit /b 0

:failed
echo.
echo ==== ビルドに失敗しました。上のエラーを確認してください ====
pause
exit /b 1
