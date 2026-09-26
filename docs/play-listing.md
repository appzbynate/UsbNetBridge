# Play Store listing copy

Use this wording. Do not promise cameras, storage, or “any USB device.”

## App name

UsbNetBridge

## Short description (≤80 characters)

Use a USB mouse or controller from a USB host on your Windows PC, over your network.

## Full description

UsbNetBridge shares a USB device from a USB host (this Android app) to a Windows PC on the same Wi‑Fi, hotspot, or VPN. The PC sees a real USB device — so a mouse moves the Windows cursor and a HID controller can drive games.

This is the right tool for:

- USB mice
- HID game controllers and steering wheels
- Other simple HID devices

Install the free Windows client from the GitHub Releases page for this project, and install usbip-win2 once (signed Windows USB/IP driver). Open this app, plug in the USB device, tap Start. The PC finds the host on the network.

Not a cloud service. Traffic stays on your network. There is no account.

What this app does not claim:

- Webcams, headsets, and other isochronous USB devices often fail on Android USB/IP. Do not buy this for a camera.
- USB storage is not a supported product path.
- The USB host and PC must be able to reach each other (same LAN, hotspot, or VPN). Cellular data is not used for sharing.

Source code is GPL-3. Corresponding source is on GitHub.

## Graphics (Play Console)

Upload files from [`play-assets/`](play-assets/README.md). Phone shots are the real Android + Windows captures with the shared device highlighted. Promo video is a YouTube URL, not the MP4 itself.

## Category

Tools / Productivity

## Data safety (Play Console)

- No user accounts
- No collected data shared with third parties
- No encryption in transit beyond your local network (LAN)
- App does not collect personal data on UsbNetBridge servers
- USB device info and local IPs are used only to share USB on the LAN

## Foreground service (special use)

When Play asks why `specialUse` is required:

> Keeps a local-network USB/IP server running so a Windows PC can use USB devices plugged into this Android USB host, including while the screen is off. Not a generic keep-alive.
