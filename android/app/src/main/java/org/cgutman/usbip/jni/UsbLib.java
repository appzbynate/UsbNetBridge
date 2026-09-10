package org.cgutman.usbip.jni;

public class UsbLib {
	static {
		System.loadLibrary("usblib");
	}

	public static native int doControlTransfer(int fd, byte requestType, byte request, short value,
											   short index, byte[] data, int length, int timeout);

	public static native int clearHalt(int fd, int endpoint);

	public static native int resetDevice(int fd);

	public static native int resetEp(int fd, int endpoint);

	public static native int disconnectKernelDriver(int fd, int interfaceNum);

	public static native int doBulkTransfer(int fd, int endpoint, byte[] data, int timeout);
}
