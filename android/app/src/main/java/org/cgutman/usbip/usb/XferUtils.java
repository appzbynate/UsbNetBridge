package org.cgutman.usbip.usb;

import org.cgutman.usbip.jni.UsbLib;

import android.hardware.usb.UsbDeviceConnection;
import android.hardware.usb.UsbEndpoint;

public class XferUtils {

	private static final boolean DEBUG = false;

	public static int doInterruptTransfer(UsbDeviceConnection devConn, UsbEndpoint endpoint, byte[] buff, int timeout) {
		// Interrupt transfers are implemented as one-shot bulk transfers
		int res = UsbLib.doBulkTransfer(devConn.getFileDescriptor(), endpoint.getAddress(), buff, timeout);
		if (res < 0 && res != -110) {
			// Don't print for ETIMEDOUT
			System.err.println("Interrupt Xfer failed: "+res);
		}
		
		return res;
	}
	
	private static final int MAX_BULK_XFER_SIZE = 16384;

	public static int doBulkTransfer(UsbDeviceConnection devConn, UsbEndpoint endpoint, byte[] buff, int timeout) {
		if (buff.length <= MAX_BULK_XFER_SIZE) {
			int res = UsbLib.doBulkTransfer(devConn.getFileDescriptor(), endpoint.getAddress(), buff, timeout);
			if (res < 0 && res != -110) {
				System.err.println("Bulk Xfer failed: "+res);
			}
			return res;
		}

		// Large transfer: split into chunks
		int fd = devConn.getFileDescriptor();
		int ep = endpoint.getAddress();
		int totalTransferred = 0;

		while (totalTransferred < buff.length) {
			int remaining = buff.length - totalTransferred;
			int chunkSize = Math.min(remaining, MAX_BULK_XFER_SIZE);

			byte[] chunk;
			if ((ep & 0x80) != 0) {
				// IN: allocate temp buffer, copy after
				chunk = new byte[chunkSize];
			} else {
				// OUT: copy data to send
				chunk = new byte[chunkSize];
				System.arraycopy(buff, totalTransferred, chunk, 0, chunkSize);
			}

			int res = UsbLib.doBulkTransfer(fd, ep, chunk, timeout);
			if (res < 0) {
				if (res != -110) {
					System.err.println("Bulk Xfer failed: "+res);
				}
				return totalTransferred > 0 ? totalTransferred : res;
			}

			if ((ep & 0x80) != 0) {
				// IN: copy received data back
				System.arraycopy(chunk, 0, buff, totalTransferred, res);
			}

			totalTransferred += res;

			// Short packet means end of transfer
			if (res < chunkSize) {
				break;
			}
		}

		return totalTransferred;
	}

	public static int doControlTransfer(UsbDeviceConnection devConn, int requestType,
			int request, int value, int index, byte[] buff, int length, int interval) {
		
		// Mask out possible sign expansions
		requestType &= 0xFF;
		request &= 0xFF;
		value &= 0xFFFF;
		index &= 0xFFFF;
		length &= 0xFFFF;
		
		if (DEBUG) {
			System.out.printf("SETUP: %02x %02x %04x %04x %04x\n",
					requestType, request, value, index, length);
		}
		
		int res = UsbLib.doControlTransfer(devConn.getFileDescriptor(), (byte)requestType, (byte)request,
				(short)value, (short)index, buff, length, interval);
		if (res < 0 && res != -110) {
			System.err.printf("Control Xfer failed: %d (SETUP: %02x %02x %04x %04x %04x)\n",
					res, requestType, request, value, index, length);
		}
		
		return res;
	}
}
