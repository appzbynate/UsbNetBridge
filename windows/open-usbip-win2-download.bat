@echo off
REM Opens the usbip-win2 releases page and shows where usbip.exe should live.
echo.
echo UsbNetBridge needs usbip-win2 (Windows virtual USB driver).
echo 1) Create a System Restore point
echo 2) Download + install from the browser window that opens
echo 3) Confirm:  "%ProgramFiles%\USBip\usbip.exe"
echo.
start "" "https://github.com/vadimgrn/usbip-win2/releases"
pause
