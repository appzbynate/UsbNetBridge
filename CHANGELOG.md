# Changelog

Each entry matches a git commit so you can revert that snapshot:

```
git log --oneline
git checkout <commit>
```

To undo one change and keep later work: `git revert <commit>`.

---

## Unreleased

_(New work lands here until the next commit.)_

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
