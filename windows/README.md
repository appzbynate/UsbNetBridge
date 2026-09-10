# UsbNetBridge — Windows client (USB/IP)

Attaches remote USB devices from the UsbNetBridge USB host so they appear as **normal Windows devices** (Device Manager / HID mouse, etc.).

Requires **[usbip-win2](https://github.com/vadimgrn/usbip-win2)** once — see [`INSTALL_USBIP_WIN2.md`](INSTALL_USBIP_WIN2.md).

## Build

```bat
cd windows
dotnet build UsbNetBridge.Client\UsbNetBridge.Client.csproj -c Release
```

The EXE is `UsbNetBridge.Client\bin\UsbNetBridge.Client.exe`. GitHub Releases ship a zip of that build.

Or open `UsbNetBridge.Client.sln` in Visual Studio 2022+.

## Sample mouse workflow

1. Install usbip-win2 (restore point first).
2. On the USB host: Start UsbNetBridge, tap Start.
3. In this app: enter IP → **Refresh devices** → select mouse → **Attach (use on this PC)**.
4. Allow USB on the phone.
5. Move the mouse — Windows cursor should move.
6. **Detach all** when finished.

If **Attach** fails with an access/driver error, try running the client **as Administrator** once, or attach via an elevated Command Prompt using `usbip.exe` (see INSTALL doc).

## Layout

| File | Role |
|------|------|
| `MainForm.cs` | UI |
| `UsbipCli.cs` | Invokes `usbip.exe` list/attach/detach |
| `Program.cs` | Entry |

## Security

LAN only. Do not expose port 3240. Prefer a private Wi‑Fi or phone hotspot.
