@echo off
setlocal
chcp 65001 >nul
pushd "%~dp0" || exit /b 1

call :CheckRuntime
if not errorlevel 1 goto Launch

if not exist "windowsdesktop-runtime-10.0.15-win-x64.exe" (
    echo 缺少运行时安装程序，请重新解压完整的发布包。
    pause
    exit /b 1
)

choice /C YN /M "缺少 .NET 10 Desktop Runtime x64。是否打开微软安装程序？"
if not "%errorlevel%"=="1" exit /b 0

start "" /wait "%~dp0windowsdesktop-runtime-10.0.15-win-x64.exe"
call :CheckRuntime
if errorlevel 1 (
    echo 尚未检测到运行时。如果安装程序要求重启，请重启后再运行。
    pause
    exit /b 1
)

:Launch
start "" "%~dp0PlcSimulator.exe"
exit /b %errorlevel%

:CheckRuntime
if defined DOTNET_ROOT_X64 (
    call :CheckDotnet "%DOTNET_ROOT_X64%\dotnet.exe"
    exit /b
)
set "dotnetRoot=%ProgramFiles%\dotnet"
if defined ProgramW6432 set "dotnetRoot=%ProgramW6432%\dotnet"
call :CheckDotnet "%dotnetRoot%\dotnet.exe"
exit /b

:CheckDotnet
if not exist "%~1" exit /b 1
"%~1" --list-runtimes 2>nul | findstr /b /l /c:"Microsoft.WindowsDesktop.App 10.0." >nul
exit /b %errorlevel%
