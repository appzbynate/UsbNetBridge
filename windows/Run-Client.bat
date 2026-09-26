@echo off
title UsbNetBridge Client
cd /d "%~dp0"

echo Closing any running UsbNetBridge.Client...
taskkill /IM UsbNetBridge.Client.exe /F >nul 2>&1

echo Removing leftover build folders...
rmdir /s /q "%~dp0UsbNetBridge.Client\bin" 2>nul


echo Building client...
dotnet build "%~dp0UsbNetBridge.Client\UsbNetBridge.Client.csproj" -c Release
if errorlevel 1 (
  echo BUILD FAILED
  pause
  exit /b 1
)

set EXE=%~dp0UsbNetBridge.Client\bin\UsbNetBridge.Client.exe
if not exist "%EXE%" (
  echo Missing EXE:
  echo   %EXE%
  pause
  exit /b 1
)

echo Starting:
echo   %EXE%
echo.
start "" "%EXE%"
