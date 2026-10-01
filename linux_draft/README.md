# UsbNetBridge - Linux Client Draft

This is a draft scaffold for the Linux version of the UsbNetBridge client. 
It uses **Python 3** and **PyQt5** to recreate the dark-mode "Soft UI" look of the Windows C# client natively on Linux.

## Why Python + PyQt5?
- PyQt5 is incredibly stable across all Linux distributions (Ubuntu, Fedora, Arch, etc.).
- It allows us to easily match the styling and dark-mode aesthetic of the Windows app using QSS (Qt Style Sheets).
- Python has robust `subprocess` management, which is perfect for parsing the command-line output of the Linux `usbip` tool.

## How USB/IP works on Linux
Unlike Windows, where we had to install a heavy third-party driver (`usbipd-win`), the `usbip` kernel modules and tools are **natively built into the Linux kernel**.
Users simply need to install the kernel tools:
```bash
sudo apt install linux-tools-generic linux-tools-$(uname -r) hwdata
sudo modprobe vhci-hcd
```

The Python GUI will simply run these native commands behind the scenes:
- **Scan:** `usbip list -r <Android_IP>`
- **Attach:** `sudo usbip attach -r <Android_IP> -b <busid>`
- **Detach:** `sudo usbip detach -p <port>`

## To Run the Draft
1. Install PyQt5: `pip install PyQt5`
2. Run the UI: `python main.py`
