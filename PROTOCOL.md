# Protocol: USB/IP v1.1.1

UsbNetBridge uses the standard **USB/IP** protocol (same family as the Linux kernel USB/IP tools), not a custom JSON API.

| Item | Value |
|------|--------|
| Default port | **3240** |
| Version | **0x0111** (1.1.1) |
| Transport | TCP, LAN |
| Android role | USB/IP **server** (export devices) |
| Windows role | USB/IP **client** via usbip-win2 VHCI driver |

## Why USB/IP (vs custom JSON)

| Approach | Result on Windows |
|----------|-------------------|
| Custom JSON transfers | Only our app can talk USB; mouse does **not** move the system cursor |
| **USB/IP + VHCI driver** | Device appears in Device Manager; HID mouse works system-wide |

## Basic operations

Client tools (`usbip.exe` from usbip-win2):

```bat
usbip.exe list -r <phone-ip>
usbip.exe attach -r <phone-ip> -b <busid>
usbip.exe port
usbip.exe detach -p <port>
```

Wire-level OP codes include `OP_REQ_DEVLIST` / `OP_REP_DEVLIST` and `OP_REQ_IMPORT` / `OP_REP_IMPORT`, then URB submit/unlink frames for ongoing I/O.

## Security

- Intended for **trusted LAN** only.
- No TLS in the classic USB/IP protocol; use a private Wi‑Fi / hotspot and do not expose port 3240 publicly.
- Optional future hardening: VPN, firewall allowlist, SSH tunnel.

## Sample session (mouse)

1. Android: Start USB/IP server → shows `192.168.x.y:3240`.
2. Windows: `usbip list -r 192.168.x.y` → see bus id.
3. `usbip attach -r 192.168.x.y -b <busid>` → phone shows USB permission → Allow.
4. Windows Device Manager shows a mouse; cursor moves.
5. `usbip detach -p 1` when finished.

Python DEVLIST helper: [`tools/test_client.py`](tools/test_client.py).
