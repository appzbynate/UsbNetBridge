#!/usr/bin/env python3
"""
List exportable USB/IP devices from an Android UsbNetBridge / USBIP server.

Uses the standard USB/IP OP_REQ_DEVLIST (v1.1.1) over TCP port 3240.
Attach still requires usbip-win2 (or Linux usbip) on the client OS.

Usage:
  python test_client.py --host 192.168.1.10
"""

from __future__ import annotations

import argparse
import socket
import struct
import sys

USBIP_VERSION = 0x0111  # 1.1.1
OP_REQ_DEVLIST = 0x8005
OP_REP_DEVLIST = 0x0005
USBIP_DEFAULT_PORT = 3240


def read_exact(sock: socket.socket, n: int) -> bytes:
    buf = bytearray()
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk:
            raise ConnectionError("connection closed")
        buf.extend(chunk)
    return bytes(buf)


def list_devices(host: str, port: int) -> list[dict]:
    # Common header: version(u16) + code(u16) + status(u32) for request status=0
    req = struct.pack(">HHI", USBIP_VERSION, OP_REQ_DEVLIST, 0)
    with socket.create_connection((host, port), timeout=10) as sock:
        sock.sendall(req)
        hdr = read_exact(sock, 8)
        version, code, status = struct.unpack(">HHI", hdr)
        if code != OP_REP_DEVLIST:
            raise RuntimeError(f"unexpected reply code 0x{code:04x}")
        if status != 0:
            raise RuntimeError(f"server status={status}")
        (ndev,) = struct.unpack(">I", read_exact(sock, 4))
        devices = []
        for _ in range(ndev):
            # usbip_usb_device layout used by Linux / Android USB/IP servers
            path = read_exact(sock, 256).split(b"\0", 1)[0].decode("utf-8", "replace")
            busid = read_exact(sock, 32).split(b"\0", 1)[0].decode("utf-8", "replace")
            busnum, devnum, speed = struct.unpack(">III", read_exact(sock, 12))
            idVendor, idProduct, bcdDevice = struct.unpack(">HHH", read_exact(sock, 6))
            (
                bDeviceClass,
                bDeviceSubClass,
                bDeviceProtocol,
                bConfigurationValue,
                bNumConfigurations,
                bNumInterfaces,
            ) = struct.unpack(">BBBBBB", read_exact(sock, 6))
            ifaces = []
            for _i in range(bNumInterfaces):
                # UsbIpInterface: class, subclass, protocol, padding
                ic, isc, ip, _pad = struct.unpack(">BBBB", read_exact(sock, 4))
                ifaces.append({"class": ic, "subclass": isc, "protocol": ip})
            devices.append(
                {
                    "path": path,
                    "busid": busid,
                    "busnum": busnum,
                    "devnum": devnum,
                    "speed": speed,
                    "idVendor": idVendor,
                    "idProduct": idProduct,
                    "bcdDevice": bcdDevice,
                    "bDeviceClass": bDeviceClass,
                    "bConfigurationValue": bConfigurationValue,
                    "bNumConfigurations": bNumConfigurations,
                    "bNumInterfaces": bNumInterfaces,
                    "interfaces": ifaces,
                }
            )
        return devices


def main() -> int:
    p = argparse.ArgumentParser(description="USB/IP device list smoke test")
    p.add_argument("--host", required=True, help="Android phone LAN IP")
    p.add_argument("--port", type=int, default=USBIP_DEFAULT_PORT)
    args = p.parse_args()

    try:
        print(f"USB/IP DEVLIST {args.host}:{args.port} …")
        devices = list_devices(args.host, args.port)
        print(f"devices: {len(devices)}")
        for d in devices:
            print(
                f"  busid={d['busid']}  "
                f"VID={d['idVendor']:04X} PID={d['idProduct']:04X}  "
                f"class={d['bDeviceClass']}  ifaces={d['bNumInterfaces']}"
            )
        if not devices:
            print("No devices — plug OTG USB and Start the Android USB/IP server.")
            return 2
        print("OK — use Windows UsbNetBridge Client / usbip.exe attach to use a device.")
        return 0
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        print(
            "Is the Android USB/IP server running? Same Wi-Fi? Port 3240 reachable?",
            file=sys.stderr,
        )
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
