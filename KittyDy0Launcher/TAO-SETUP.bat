@echo off
setlocal
cd /d "%~dp0"
title Tao Setup.exe cho KittyDy0 Launcher

echo ==============================================
echo  Tao KittyDy0Launcher-Setup.exe - tu dong  [script v3]
echo ==============================================
echo.

rem ---------- 0. Kiem tra da giai nen day du chua ----------
if not exist "KittyDy0Launcher.csproj" (
  echo LOI: Khong thay file KittyDy0Launcher.csproj canh file TAO-SETUP.bat.
  echo Hay giai nen day du file zip ra mot thu muc roi chay lai.
  pause
  exit /b 1
)

if not exist "Assets\icon.ico" (
  echo LOI: Thieu thu muc Assets\icon.ico - day la BAN CU, se khong co icon.
  echo Hay tai lai file zip moi nhat, giai nen vao mot thu muc TRONG roi chay lai.
  pause
  exit /b 1
)

rem ---------- 1. Tim .NET SDK ----------
set "DOTNET="
where dotnet >nul 2>nul
if not errorlevel 1 set "DOTNET=dotnet"
if not defined DOTNET if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
if not defined DOTNET (
  echo Khong tim thay .NET SDK. Hay cai .NET 8 SDK tai:
  echo https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)
echo Dung .NET SDK:
"%DOTNET%" --version

rem ---------- 2. Build ban doc lap da kem san .NET va moi thu vien ----------
echo.
echo [2/3] Dang build launcher...
if exist publish rmdir /s /q publish
"%DOTNET%" publish "KittyDy0Launcher.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if errorlevel 1 (
  echo.
  echo BUILD LOI. Hay chup man hinh loi o tren gui lai de duoc sua.
  pause
  exit /b 1
)

rem ---------- 3. Tim Inno Setup ----------
echo.
echo [3/3] Dang tao Setup.exe...
set "ISCC="
call :findiscc
if defined ISCC goto runiscc

echo Chua tim thay Inno Setup, dang thu tu cai dat...
where winget >nul 2>nul
if not errorlevel 1 goto viawinget
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest 'https://jrsoftware.org/download.php/is.exe' -OutFile '%TEMP%\innosetup.exe'; Start-Process '%TEMP%\innosetup.exe' -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER' -Wait"
goto afterinstall
:viawinget
winget install -e --id JRSoftware.InnoSetup --accept-package-agreements --accept-source-agreements
:afterinstall
call :findiscc
if defined ISCC goto runiscc

echo.
echo Khong tu tim thay Inno Setup tren may.
echo Hay tim file ISCC.exe: mo thu muc Inno Setup da cai, tim file ISCC.exe,
echo roi KEO THA file do vao cua so nay va nhan Enter.
echo.
set /p "ISCC=Duong dan toi ISCC.exe: "
call :stripq
if not exist "%ISCC%" (
  echo Duong dan khong dung. Ban van dung duoc file publish\KittyDy0Launcher.exe truc tiep.
  pause
  exit /b 1
)

:runiscc
echo Dung Inno Setup: %ISCC%
"%ISCC%" installer.iss
if errorlevel 1 (
  echo Tao Setup.exe bi loi.
  pause
  exit /b 1
)

echo.
echo ==============================================
echo  XONG! File cai dat nam trong thu muc Output:
echo  Output\KittyDy0Launcher-Setup.exe
echo ==============================================
start "" "%~dp0Output"
pause
exit /b 0

:stripq
set "ISCC=%ISCC:"=%"
exit /b 0

:findiscc
rem Tim trong moi thu muc Inno Setup, ke ca ban 5, 6, 7
for /d %%D in ("%ProgramFiles(x86)%\Inno Setup*") do if exist "%%D\ISCC.exe" set "ISCC=%%D\ISCC.exe"
for /d %%D in ("%ProgramFiles%\Inno Setup*") do if exist "%%D\ISCC.exe" set "ISCC=%%D\ISCC.exe"
for /d %%D in ("%LOCALAPPDATA%\Programs\Inno Setup*") do if exist "%%D\ISCC.exe" set "ISCC=%%D\ISCC.exe"
exit /b 0
