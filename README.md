# UsbNetBridge

Use a USB device from a **USB host** on a Windows PC — over your local network. Similar in spirit to VirtualHere, without a cloud service.

Today the USB host app runs on **Android** (USB OTG). The Windows client talks USB/IP; other host OSes could be added later.

| Piece | Path | Role |
|-------|------|------|
| USB host app (Android) | [`android/`](android/) | Shares OTG devices on TCP **3240** (no root) |
| Windows client | [`windows/`](windows/) | UI over **usbip-win2** attach/detach |
| Driver (required once) | [usbip-win2](https://github.com/vadimgrn/usbip-win2/releases) | Virtual USB bus so Windows loads real drivers |
| Protocol | [`PROTOCOL.md`](PROTOCOL.md) | USB/IP v1.1.1 |

**License:** [GPL-3.0](LICENSE) (the Android USB/IP stack is adapted from [USBIPServerForAndroid](https://github.com/cgutman/USBIPServerForAndroid); see [NOTICE](NOTICE)).  
**Privacy:** [PRIVACY.md](PRIVACY.md) · [Play listing notes](docs/play-listing.md) · Source: [github.com/appzbynate/UsbNetBridge](https://github.com/appzbynate/UsbNetBridge)

## What works

HID is the product: **USB mice**, **game controllers**, and **steering wheels** on the same Wi‑Fi, hotspot, or VPN.

Isochronous devices (many webcams and headsets) often fail. USB storage is not a supported path.

## Quick start

1. **Windows (once):** restore point, then install usbip-win2 — [`windows/INSTALL_USBIP_WIN2.md`](windows/INSTALL_USBIP_WIN2.md).
2. **USB host:** install the Android app, plug in a USB device, tap **Start**.
3. **Windows client:** run UsbNetBridge.Client. It finds the host on the network. Double-click a device to use it on this PC.
4. On the host, **Allow** USB permission when prompted.

## Privacy

- Traffic stays on your LAN (or phone hotspot / VPN).
- Do **not** port-forward TCP 3240 to the internet.
- No cloud account. See [PRIVACY.md](PRIVACY.md).

## Credits

Android USB/IP implementation adapted from [USBIPServerForAndroid](https://github.com/cgutman/USBIPServerForAndroid) / [andrewtakeshi fork](https://github.com/andrewtakeshi/USBIPServerForAndroid).  
Windows virtual USB via [usbip-win2](https://github.com/vadimgrn/usbip-win2).
