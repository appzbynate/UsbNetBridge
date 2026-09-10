# Disable USB selective suspend (helps keep attached USB/IP mice alive)
# Run in an elevated PowerShell: Right-click PowerShell → Run as administrator

powercfg /SETACVALUEINDEX SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0
powercfg /SETDCVALUEINDEX SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0
powercfg /SetActive SCHEME_CURRENT

Write-Host "USB selective suspend disabled for the current power plan."
Write-Host "Also in Device Manager: Universal Serial Bus controllers → each USB Root Hub"
Write-Host "  → Properties → Power Management → uncheck 'Allow the computer to turn off this device'"
