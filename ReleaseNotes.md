# UsbNetBridge v2.1.0

## What's New in this Release

**✨ Android Updates:**
- **Visual Overhaul:** The Stop button now glows a solid, bright Danger Red when the server is active, making it much more obvious at a glance.
- **Fixed Multi-Device Status:** Fixed a bug where connecting multiple devices to the same PC would cause the Android UI to incorrectly show them as "Ready". The Android app now explicitly tracks each active device and displays "In use on [PC]" correctly for exactly the hardware you're sharing.

**✨ Windows Client Updates:**
- **Permanent Anti-Spam Auto-Connect:** Fixed an annoying bug where Android dropping a background Wi-Fi broadcast packet would trick the Windows app into repeatedly spamming the phone with USB permission requests. The holdoff list is now permanent—once you reject or cancel a device on the phone, Windows will never attempt to Auto-Connect to it again until you click it manually or restart the app.
- **Added Installation Wizard:** The Windows client is now distributed with a professional `Setup.exe`! You can now install UsbNetBridge like a standard Windows app, complete with Start Menu shortcuts, a Desktop Icon option, and a checkbox to automatically "Start with Windows".
- **Added Context Menus for Technical Info:** To keep the primary UI incredibly clean for standard users, all the highly technical USB info (VID/PID, raw IP addresses, and Bus IDs) has been moved out of the main list. You can now right-click any Server or USB Device and select "Host info..." or "Device info..." to view the exact hardware strings for debugging.
- **Improved UI Compactness:** Significantly reduced the height of UI rows, allowing you to comfortably view many more USB devices simultaneously on a single screen without scrolling.
- **Multi-Device Hub Support:** Removed an artificial restriction that forced the client to disconnect all devices before connecting to a new one. You can now actively connect to multiple USB devices concurrently from the same remote phone/hub.

## Downloads
- **`UsbNetBridge_Setup.exe`** - Recommended for most users. Includes a standard installer, desktop icon, and "Start with Windows" functionality.
- **`UsbNetBridge_Portable.zip`** - Standalone `.exe` for power users who prefer no installation. Extract and run `UsbNetBridge.Client.exe`.

## How to Install the Windows Client
1. Download `UsbNetBridge_Setup.exe` below.
2. Run the installer and follow the wizard.
3. Once installed, simply launch the app, ensure your Android device is running the UsbNetBridge server on the same network, and click a device to connect!
