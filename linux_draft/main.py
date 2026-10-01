import sys
import os
import subprocess
from PyQt5.QtWidgets import (QApplication, QMainWindow, QWidget, QVBoxLayout, 
                             QHBoxLayout, QLabel, QLineEdit, QPushButton, 
                             QListWidget, QGroupBox, QMessageBox)
from PyQt5.QtCore import Qt, QTimer
from PyQt5.QtGui import QFont, QIcon, QPixmap

class UsbNetBridgeLinux(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("UsbNetBridge - Linux")
        self.setMinimumSize(850, 600)
        self.setStyleSheet("""
            QMainWindow { background-color: #1a1a1a; }
            QLabel { color: #FFFFFF; font-size: 14px; }
            QGroupBox { border: 1px solid #333333; border-radius: 6px; margin-top: 10px; color: #00E5FF; font-weight: bold; }
            QGroupBox::title { subcontrol-origin: margin; left: 10px; padding: 0 3px; }
            QPushButton { background-color: #0078D7; color: white; border-radius: 4px; padding: 8px 16px; font-weight: bold; }
            QPushButton:hover { background-color: #005A9E; }
            QLineEdit { background-color: #2D2D2D; color: #FFFFFF; border: 1px solid #444444; border-radius: 4px; padding: 6px; }
            QListWidget { background-color: #1E1E1E; color: #FFFFFF; border: 1px solid #333333; border-radius: 4px; padding: 4px; }
        """)

        central = QWidget()
        self.setCentralWidget(central)
        main_layout = QVBoxLayout(central)
        main_layout.setContentsMargins(20, 20, 20, 20)
        main_layout.setSpacing(15)

        # Header
        header_layout = QHBoxLayout()
        title = QLabel("UsbNetBridge")
        title.setStyleSheet("font-size: 24px; font-weight: bold; color: #00E5FF;")
        header_layout.addWidget(title)
        header_layout.addStretch()
        logs_btn = QPushButton("Logs")
        logs_btn.setStyleSheet("background-color: #333333;")
        header_layout.addWidget(logs_btn)
        main_layout.addLayout(header_layout)

        # Connect Section
        connect_layout = QHBoxLayout()
        self.host_input = QLineEdit()
        self.host_input.setPlaceholderText("Enter Phone IP (e.g. 192.168.1.50)")
        connect_layout.addWidget(self.host_input)
        self.refresh_btn = QPushButton("Find & Connect")
        connect_layout.addWidget(self.refresh_btn)
        main_layout.addLayout(connect_layout)

        # Body Lists
        lists_layout = QHBoxLayout()
        
        servers_group = QGroupBox("Available Servers")
        servers_layout = QVBoxLayout()
        self.servers_list = QListWidget()
        servers_layout.addWidget(self.servers_list)
        servers_group.setLayout(servers_layout)
        lists_layout.addWidget(servers_group)

        devices_group = QGroupBox("Remote Devices")
        devices_layout = QVBoxLayout()
        self.devices_list = QListWidget()
        devices_layout.addWidget(self.devices_list)
        self.attach_btn = QPushButton("Attach Selected")
        devices_layout.addWidget(self.attach_btn)
        devices_group.setLayout(devices_layout)
        lists_layout.addWidget(devices_group)

        attached_group = QGroupBox("Attached Locally")
        attached_layout = QVBoxLayout()
        self.attached_list = QListWidget()
        attached_layout.addWidget(self.attached_list)
        self.detach_btn = QPushButton("Detach Selected")
        self.detach_btn.setStyleSheet("background-color: #C50F1F;")
        attached_layout.addWidget(self.detach_btn)
        attached_group.setLayout(attached_layout)
        lists_layout.addWidget(attached_group)

        main_layout.addLayout(lists_layout)

        # Signals
        self.refresh_btn.clicked.connect(self.scan_devices)
        self.attach_btn.clicked.connect(self.attach_device)
        self.detach_btn.clicked.connect(self.detach_device)

    def scan_devices(self):
        host = self.host_input.text().strip()
        if not host:
            QMessageBox.warning(self, "Error", "Please enter a valid IP address")
            return
        self.devices_list.clear()
        try:
            # On Linux, usbip is built-in. This uses standard usbip commands.
            result = subprocess.run(["usbip", "list", "-r", host], capture_output=True, text=True, timeout=5)
            for line in result.stdout.split('\\n'):
                if ":" in line:
                    self.devices_list.addItem(line.strip())
        except Exception as e:
            self.devices_list.addItem(f"usbip command error (install linux-tools): {str(e)}")

    def attach_device(self):
        QMessageBox.information(self, "Not Implemented", "Requires sudo usbip attach -r <host> -b <busid>")

    def detach_device(self):
        QMessageBox.information(self, "Not Implemented", "Requires sudo usbip detach -p <port>")

if __name__ == "__main__":
    app = QApplication(sys.argv)
    window = UsbNetBridgeLinux()
    window.show()
    sys.exit(app.exec_())
