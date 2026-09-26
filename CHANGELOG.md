# Changelog

Each entry matches a git commit so you can revert that snapshot:

```
git log --oneline
git checkout <commit>
```

To undo one change and keep later work: `git revert <commit>`.

---

## Unreleased

- Performance: gate per-control-URB logging behind DEBUG and decouple `updateServerUi()` from `MainActivity.logListener` to prevent IPC Binder and network interface queries from causing HID mouse stutter.
- Discovery: set `SO_REUSEADDR` before binding UDP port 3241 in `DiscoveryBeacon` to prevent `BindException` on quick service restarts.
- Robustness: add bounds check on `transferBufferLength` (`0..65536`) in `UsbIpSubmitUrb` to prevent negative allocations and uncaught `OutOfMemoryError`.
- Fast unplug: tombstone recently unplugged USB IDs on Android and Windows to prevent stale device re-appearance in Available; immediately unhide on `ACTION_USB_DEVICE_ATTACHED` and tune Windows tombstone window to 3.5s for snappy re-plugs.
- Threading: cap UI thread `requestPool.awaitTermination` to 1.5s during unplug teardown to prevent ANR risk.
- UI Polish (Android): structured device cards with USB icon, bold product name, monospace metadata pill, and color-coded status badges (Ready, Tap to Allow, In use on PC) with tap-to-request permission support; pill-shaped action buttons (24dp radius); polished status banner and headers.
- UI Polish (Windows): sleek dark crimson Disconnect button palette; softened card border and glow pens for a modern glassmorphic look; glowing cyan 3px left accent indicator on selected list rows; tighter hero banner height.
- Device Naming Parity: unified `DiscoveryBeacon.friendlyUsbName` companion helper as single source of truth across Android beacon broadcasting, Android UI card rendering, and Windows client DisplayName.

## 2.0.0 — 2026-09-10

First public GPL-3 snapshot for GitHub and Play.

- Windows client: discovery, attach/detach, auto-connect, USB-host wording.
- Android host: Start/Stop, LAN beacons, foreground USB/IP service.
- Play-oriented target: API 36, NDK r28 / 16KB pages, signed AAB when `keystore.properties` is present.
- HID (mouse, wheel, controllers) is the supported path; cameras/storage are not claimed.

## 2026-08-16 — Product client and server

Windows discovery, USB/IP attach UI, auto-connect, and Android Start/Stop behavior.

- USB plug-in opens the Android app; Start is always manual (no auto-start after Stop).
- Stop and last-device unplug notify Windows immediately (`UNB1-off` / `UNB1-gone`).
- Right-click a device to always auto-connect; same menu to turn it off. **Auto** chip shows next to **Connected**.
- Play-oriented permission cuts: dropped `ACCESS_NETWORK_STATE` and `REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`; kept multicast lock (needed for a stable link).
- Cellular IPs hidden from the phone UI and discovery.

## 2026-08-16 — Initial scaffold

First snapshot of the Android USB/IP server and Windows usbip-win2 client.
