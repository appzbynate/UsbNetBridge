# UsbNetBridge — Android USB/IP server

Shares OTG USB devices over your LAN using **USB/IP** (port **3240**). No root. Based on USBIPServerForAndroid.

## Requirements

- Android Studio + **NDK r28+** (16KB page size; sync will prompt if missing)
- Phone with USB Host / OTG
- Same Wi‑Fi (or hotspot) as the PC

## Build / run

1. Open `android/` in Android Studio.
2. Install NDK if prompted (**Tools → SDK Manager → SDK Tools → NDK**).
3. Sync Gradle → Run on device.

CLI (after `local.properties` has `sdk.dir`):

```bat
cd android
gradlew.bat assembleDebug
adb install -r app\build\outputs\apk\debug\app-debug.apk
```

## Use

1. Plug a USB device (e.g. mouse) via OTG.
2. Tap **Start** (allow notifications).
3. Copy the shown **IP:3240** into the Windows client.
4. When Windows **Attach**es, tap **Allow** on the Android USB permission dialog.

## Layout

| Path | Role |
|------|------|
| `com.usbnetbridge.server.MainActivity` | UI: start/stop, IP display, device list |
| `org.cgutman.usbip.service.UsbIpService` | Foreground USB/IP server |
| `org.cgutman.usbip.server.*` | Protocol + URB handling |
| `jni/usblib` | Native ioctl helpers (clear halt, bulk, etc.) |

## Limitations

- Isochronous (many cameras/mics) not supported.
- Device must stay permitted while attached.
- Keep the foreground notification while serving.
