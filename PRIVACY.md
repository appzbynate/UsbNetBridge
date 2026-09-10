# Privacy policy — UsbNetBridge

Last updated: 10 September 2026

UsbNetBridge shares USB devices from a USB host (today: an Android device with USB OTG) to a Windows PC on the **same local network**. It does not use a cloud account.

## What the Android app does

- Listens on your local network (USB/IP on TCP 3240, discovery on UDP 3241, events on UDP 3242) so the Windows client can find this device and attach USB devices you plug in.
- Shows this device’s local IP address in the app so you can connect if automatic discovery fails.
- Uses a foreground notification while sharing is on, so the server can keep running.
- Asks for USB permission when you plug in a device. USB access stays on this device unless you tap Allow.

## Data we collect

**We do not collect, sell, or send your data to UsbNetBridge servers.** There is no account, no analytics SDK, and no advertising.

Information that stays on your devices / LAN:

- USB device names and identifiers (needed so the PC can attach the right device)
- Local IP addresses of the USB host and the PC
- Optional saved PC addresses and auto-connect preferences on the Windows client (`%LocalAppData%\UsbNetBridge\`)

Anyone on the same network who runs a USB/IP client could try to attach a device while the Android app is **Started**. Only start sharing on a network you trust. Do not port-forward TCP 3240 to the internet.

## Permissions (Android)

| Permission | Why |
|------------|-----|
| Internet | Talk to the PC on your LAN |
| Wi‑Fi / multicast | Find the app from the PC on the local network |
| USB host | Read/write the plugged-in USB device |
| Foreground service (special use) | Keep sharing alive while the screen is off |
| Notifications | Show that sharing is running |

## Children

UsbNetBridge is not directed at children and does not knowingly collect personal information from children.

## Contact

Open an issue on the project’s GitHub repository (linked from the app listing and from this source tree).
