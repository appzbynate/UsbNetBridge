# Install usbip-win2 (required once)

UsbNetBridge's Windows client needs the **usbip-win2** virtual USB driver so a phone-attached mouse appears as a real Windows mouse.

## Steps

1. **Create a System Restore point** (Windows search: "Create a restore point").
2. Download the latest installer from:  
   https://github.com/vadimgrn/usbip-win2/releases
3. Run the installer as Administrator.  
   USB hubs may briefly restart during install.
4. Confirm `usbip.exe` exists, typically at:  
   `C:\Program Files\USBip\usbip.exe`
5. Restart **UsbNetBridge Client**. The green status line should show the usbip path.

## Quick CLI check (optional)

```bat
"C:\Program Files\USBip\usbip.exe" list -r <phone-ip>
"C:\Program Files\USBip\usbip.exe" attach -r <phone-ip> -b <busid>
"C:\Program Files\USBip\usbip.exe" port
"C:\Program Files\USBip\usbip.exe" detach -p 1
```

## Notes

- Drivers are WHLK-certified in recent usbip-win2 releases; follow the project README if Windows asks about test signing.
- Uninstall via Windows Apps settings if you need to remove it.
- Do not port-forward TCP 3240 to the internet.
