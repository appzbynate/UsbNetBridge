package org.cgutman.usbip.service;

import java.io.IOException;
import java.net.Socket;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.ThreadPoolExecutor;
import java.util.concurrent.TimeUnit;

import org.cgutman.usbip.server.UsbDeviceInfo;
import org.cgutman.usbip.server.UsbIpServer;
import org.cgutman.usbip.server.UsbRequestHandler;
import org.cgutman.usbip.server.protocol.ProtoDefs;
import org.cgutman.usbip.server.protocol.UsbIpDevice;
import org.cgutman.usbip.server.protocol.UsbIpInterface;
import org.cgutman.usbip.server.protocol.dev.UsbIpDevicePacket;
import org.cgutman.usbip.server.protocol.dev.UsbIpSubmitUrb;
import org.cgutman.usbip.server.protocol.dev.UsbIpSubmitUrbReply;
import org.cgutman.usbip.server.protocol.dev.UsbIpUnlinkUrb;
import org.cgutman.usbip.server.protocol.dev.UsbIpUnlinkUrbReply;
import org.cgutman.usbip.usb.UsbControlHelper;
import org.cgutman.usbip.usb.UsbDeviceDescriptor;
import org.cgutman.usbip.usb.XferUtils;

import com.usbnetbridge.server.MainActivity;
import com.usbnetbridge.server.R;

import android.annotation.SuppressLint;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.hardware.usb.UsbConfiguration;
import android.hardware.usb.UsbConstants;
import android.hardware.usb.UsbDevice;
import android.hardware.usb.UsbDeviceConnection;
import android.hardware.usb.UsbEndpoint;
import android.hardware.usb.UsbInterface;
import android.hardware.usb.UsbManager;
import android.net.wifi.WifiManager;
import android.net.wifi.WifiManager.WifiLock;
import android.os.Build;
import android.os.IBinder;
import android.os.PowerManager;
import android.os.PowerManager.WakeLock;
import android.util.SparseArray;

import androidx.core.app.NotificationCompat;
import androidx.core.content.ContextCompat;

public class UsbIpService extends Service implements UsbRequestHandler {
	
	public static volatile boolean isRunning = false;

	/** How many devices are actively attached by a USB/IP client right now. */
	public static int getSharedDeviceCount() {
		return sharedCount;
	}

	/** Windows PC currently using a device, or empty. */
	public static String getSharingClientName() {
		return sharingClientName;
	}

	public static String getSharingClientIp() {
		return sharingClientIp;
	}

	public static final Set<Integer> activeDeviceIds = ConcurrentHashMap.newKeySet();

	private static volatile int sharedCount = 0;
	private static volatile String sharingClientName = "";
	private static volatile String sharingClientIp = "";
	/** OEM getDeviceList() can still contain a device for a second after DETACHED. */
	private static final Set<Integer> recentlyUnpluggedIds = ConcurrentHashMap.newKeySet();

	public static boolean isRecentlyUnplugged(int deviceId) {
		return recentlyUnpluggedIds.contains(deviceId);
	}
	
	private UsbManager usbManager;
	
	private SparseArray<AttachedDeviceContext> connections;
	private SparseArray<Boolean> permission;
	private HashMap<Socket, AttachedDeviceContext> socketMap;
	private UsbIpServer server;
	private WakeLock cpuWakeLock;
	private WifiLock highPerfWifiLock;
	private WifiLock lowLatencyWifiLock;
	private com.usbnetbridge.server.DiscoveryBeacon discoveryBeacon;
	
	private static final boolean DEBUG = false;

	private static void appLog(String msg) {
		android.util.Log.i("UsbIpService", msg);
		try {
			com.usbnetbridge.server.AppLog.INSTANCE.append(msg);
		} catch (Throwable ignored) {
		}
	}
	
	private static final int NOTIFICATION_ID = 100;

	private final static String CHANNEL_ID = "serviceInfo";
	
	private static final String ACTION_USB_PERMISSION =
		    "org.cgutman.usbip.USB_PERMISSION";
	public static final String ACTION_STOP = "com.usbnetbridge.server.STOP";
	private PendingIntent usbPermissionIntent;
	private final BroadcastReceiver usbReceiver = new BroadcastReceiver() {
		public void onReceive(Context context, Intent intent) {
			String action = intent.getAction();
			if (ACTION_USB_PERMISSION.equals(action)) {
				UsbDevice dev;
				if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
					dev = intent.getParcelableExtra(UsbManager.EXTRA_DEVICE, UsbDevice.class);
				} else {
					dev = intent.getParcelableExtra(UsbManager.EXTRA_DEVICE);
				}
				if (dev == null) return;

				synchronized (permission) {
					permission.put(dev.getDeviceId(), intent.getBooleanExtra(UsbManager.EXTRA_PERMISSION_GRANTED, false));
					permission.notifyAll();
				}
			} else if (UsbManager.ACTION_USB_DEVICE_DETACHED.equals(action)) {
				UsbDevice dev;
				if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
					dev = intent.getParcelableExtra(UsbManager.EXTRA_DEVICE, UsbDevice.class);
				} else {
					dev = intent.getParcelableExtra(UsbManager.EXTRA_DEVICE);
				}
				if (dev == null) return;
				final int detachedId = dev.getDeviceId();
				recentlyUnpluggedIds.add(detachedId);
				if (discoveryBeacon != null)
					discoveryBeacon.hideUsbDevice(detachedId);
				System.err.println("USB DETACHED: " + dev.getDeviceName());
				String msg = "USB device detached: " +
						(dev.getProductName() != null ? dev.getProductName() : dev.getDeviceName());
				com.usbnetbridge.server.AppLog.INSTANCE.append(msg);
				ClientEventSink sink = eventSink;
				if (sink != null) {
					sink.onUsbIpEvent(msg);
				}
				cleanupDetachedDevice(detachedId, dev, true);
				stopServerIfNoUsbDevicesLeft();
				// Some OEMs keep the device in getDeviceList() briefly after DETACHED.
				new android.os.Handler(android.os.Looper.getMainLooper()).postDelayed(() -> {
					recentlyUnpluggedIds.remove(detachedId);
					if (discoveryBeacon != null)
						discoveryBeacon.unhideUsbDevice(detachedId);
					stopServerIfNoUsbDevicesLeft();
				}, 2500);
			} else if (UsbManager.ACTION_USB_DEVICE_ATTACHED.equals(action)) {
				UsbDevice dev;
				if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
					dev = intent.getParcelableExtra(UsbManager.EXTRA_DEVICE, UsbDevice.class);
				} else {
					dev = intent.getParcelableExtra(UsbManager.EXTRA_DEVICE);
				}
				if (dev != null) {
					recentlyUnpluggedIds.remove(dev.getDeviceId());
					if (discoveryBeacon != null) {
						discoveryBeacon.unhideUsbDevice(dev.getDeviceId());
					}
					String msg = "USB device attached: " +
							(dev.getProductName() != null ? dev.getProductName() : dev.getDeviceName());
					com.usbnetbridge.server.AppLog.INSTANCE.append(msg);
					if (discoveryBeacon != null) discoveryBeacon.refreshUsbCache();
					ClientEventSink sink = eventSink;
					if (sink != null) {
						sink.onUsbIpEvent(msg);
					}
				}
			}
		}
	};
	
	private void updateNotification() {
		Intent intent = new Intent(this, MainActivity.class);
		intent.setFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP);

		int intentFlags = 0;
		if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
			intentFlags |= PendingIntent.FLAG_IMMUTABLE;
		}

		PendingIntent pendIntent = PendingIntent.getActivity(this, 0, intent, intentFlags);

		NotificationCompat.Builder builder = new NotificationCompat.Builder(getApplicationContext(), CHANNEL_ID)
				.setSmallIcon(R.drawable.ic_launcher)
				.setOngoing(true)
				.setSilent(true)
				.setTicker(getString(R.string.notification_running))
				.setContentTitle(getString(R.string.notification_running))
				.setAutoCancel(false)
				.setContentIntent(pendIntent)
				.setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE);
		
		if (connections.size() == 0) {
			builder.setContentText(getString(R.string.notification_idle));
		}
		else if (!sharingClientName.isEmpty()) {
			builder.setContentText(getString(R.string.status_on_pc, sharingClientName));
		}
		else {
			builder.setContentText(getString(R.string.status_sharing, connections.size()));
		}
		sharedCount = connections.size();
		publishShareState();

		android.app.Notification notification = builder.build();
		if (Build.VERSION.SDK_INT >= 34) {
			startForeground(NOTIFICATION_ID, notification,
					android.content.pm.ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
		} else {
			startForeground(NOTIFICATION_ID, notification);
		}
	}
	
	@SuppressLint("UseSparseArrays")
	@Override
	public void onCreate() {
		super.onCreate();

		usbManager = (UsbManager) getSystemService(Context.USB_SERVICE);

		// Create notification channel before any startForeground() call.
		if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
			NotificationChannel channel = new NotificationChannel(
					CHANNEL_ID,
					getString(R.string.channel_name),
					NotificationManager.IMPORTANCE_LOW);
			channel.setDescription(getString(R.string.channel_description));
			NotificationManager notificationManager = getSystemService(NotificationManager.class);
			notificationManager.createNotificationChannel(channel);
		}

		// Refuse to run with nothing to share. When launched via startForegroundService()
		// we must still enter the foreground briefly before stopSelf().
		if (usbManager == null || usbManager.getDeviceList().isEmpty()) {
			isRunning = false;
			sharedCount = 0;
			sharingClientName = "";
			sharingClientIp = "";
			com.usbnetbridge.server.AppLog.INSTANCE.append(
					getString(R.string.need_usb_to_start));
			try {
				showTransientNotification(getString(R.string.need_usb_to_start));
			} catch (Exception ignored) {
			}
			stopSelf();
			return;
		}

		isRunning = true;
		
		// Initialize fields
		connections = new SparseArray<>();
		activeDeviceIds.clear();
		permission = new SparseArray<>();
		socketMap = new HashMap<>();

		int intentFlags = 0;
		if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
			// This PendingIntent must be mutable to allow the framework to populate EXTRA_DEVICE and EXTRA_PERMISSION_GRANTED.
			intentFlags |= PendingIntent.FLAG_MUTABLE;
		}

		Intent i = new Intent(ACTION_USB_PERMISSION);
		i.setPackage(getPackageName());

		usbPermissionIntent = PendingIntent.getBroadcast(this, 0, i, intentFlags);
		
		// Call startForeground() early to comply with Android 14+ timing requirements
		try {
			updateNotification();
		} catch (Exception e) {
			// Handle ForegroundServiceStartNotAllowedException (API 31+) and
			// InvalidForegroundServiceTypeException (API 34+)
			if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S &&
				e.getClass().getName().equals("android.app.ForegroundServiceStartNotAllowedException")) {
				System.err.println("Failed to start foreground service: ForegroundServiceStartNotAllowedException - " + e.getMessage());
				stopSelf();
				return;
			} else if (Build.VERSION.SDK_INT >= 34 &&
				e.getClass().getName().equals("android.app.ForegroundServiceTypeException")) {
				System.err.println("Failed to start foreground service: InvalidForegroundServiceTypeException - " + e.getMessage());
				stopSelf();
				return;
			}
			// Re-throw if it's a different exception
			throw e;
		}
		
		// Register broadcast receiver (permission + USB plug events)
		IntentFilter filter = new IntentFilter(ACTION_USB_PERMISSION);
		filter.addAction(UsbManager.ACTION_USB_DEVICE_ATTACHED);
		filter.addAction(UsbManager.ACTION_USB_DEVICE_DETACHED);
		ContextCompat.registerReceiver(this, usbReceiver, filter, ContextCompat.RECEIVER_NOT_EXPORTED);
		
		// Acquire wake locks
		PowerManager pm = (PowerManager) getSystemService(Context.POWER_SERVICE);
		WifiManager wm = (WifiManager) getApplicationContext().getSystemService(Context.WIFI_SERVICE);
		
		cpuWakeLock = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "UsbNetBridge:Cpu");
		cpuWakeLock.setReferenceCounted(false);
		cpuWakeLock.acquire();

		// Keep Wi‑Fi / CPU awake; screen can sleep (OTG stays powered on many phones
		// while the foreground service holds a partial wake lock).
		
		// Acquire Wi-Fi lock based on API level
		if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
			lowLatencyWifiLock = wm.createWifiLock(WifiManager.WIFI_MODE_FULL_LOW_LATENCY, "UsbNetBridge:WifiLL");
			lowLatencyWifiLock.setReferenceCounted(false);
			lowLatencyWifiLock.acquire();
			// Some OEMs still sleep on LOW_LATENCY alone — also grab HIGH_PERF if possible.
			try {
				highPerfWifiLock = wm.createWifiLock(WifiManager.WIFI_MODE_FULL_HIGH_PERF, "UsbNetBridge:WifiHP");
				highPerfWifiLock.setReferenceCounted(false);
				highPerfWifiLock.acquire();
			} catch (Exception ignored) {
			}
		} else {
			highPerfWifiLock = wm.createWifiLock(WifiManager.WIFI_MODE_FULL_HIGH_PERF, "UsbNetBridge:WifiHP");
			highPerfWifiLock.setReferenceCounted(false);
			highPerfWifiLock.acquire();
		}
		
		// Start the TCP server
		server = new UsbIpServer();
		server.setEventListener(msg -> {
			android.util.Log.i("UsbIpService", msg);
			com.usbnetbridge.server.AppLog.INSTANCE.append(msg);
			ClientEventSink sink = eventSink;
			if (sink != null) {
				sink.onUsbIpEvent(msg);
			}
		});
		server.start(this);
		com.usbnetbridge.server.AppLog.INSTANCE.append("USB/IP service started");

		discoveryBeacon = new com.usbnetbridge.server.DiscoveryBeacon(
				this,
				UsbIpServer.PORT,
				msg -> {
					com.usbnetbridge.server.AppLog.INSTANCE.append(msg);
					return kotlin.Unit.INSTANCE;
				});
		discoveryBeacon.start();
	}

	@Override
	public int onStartCommand(Intent intent, int flags, int startId) {
		if (intent != null && ACTION_STOP.equals(intent.getAction())) {
			announceOfflineAndStop();
			return START_NOT_STICKY;
		}
		if (!isRunning) {
			return START_NOT_STICKY;
		}
		if (usbManager != null && usbManager.getDeviceList().isEmpty()) {
			stopServerIfNoUsbDevicesLeft();
			return START_NOT_STICKY;
		}
		return START_NOT_STICKY;
	}

	private void showTransientNotification(String text) {
		Intent intent = new Intent(this, MainActivity.class);
		intent.setFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP);
		int intentFlags = 0;
		if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
			intentFlags |= PendingIntent.FLAG_IMMUTABLE;
		}
		PendingIntent pendIntent = PendingIntent.getActivity(this, 0, intent, intentFlags);
		NotificationCompat.Builder builder = new NotificationCompat.Builder(getApplicationContext(), CHANNEL_ID)
				.setSmallIcon(R.drawable.ic_launcher)
				.setOngoing(false)
				.setSilent(true)
				.setContentTitle("UsbNetBridge")
				.setContentText(text)
				.setContentIntent(pendIntent)
				.setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE);
		android.app.Notification notification = builder.build();
		if (Build.VERSION.SDK_INT >= 34) {
			startForeground(NOTIFICATION_ID, notification,
					android.content.pm.ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
		} else {
			startForeground(NOTIFICATION_ID, notification);
		}
	}

	private boolean hasLiveUsbDevices() {
		if (usbManager == null) return false;
		for (UsbDevice d : usbManager.getDeviceList().values()) {
			if (!recentlyUnpluggedIds.contains(d.getDeviceId()))
				return true;
		}
		return false;
	}

	/** Stop sharing when the phone has no USB devices left to export. */
	private void stopServerIfNoUsbDevicesLeft() {
		if (!isRunning || usbManager == null) return;
		if (hasLiveUsbDevices()) return;
		com.usbnetbridge.server.AppLog.INSTANCE.append(
				getString(R.string.server_stopped_no_usb));
		ClientEventSink sink = eventSink;
		if (sink != null) {
			sink.onUsbIpEvent(getString(R.string.server_stopped_no_usb));
		}
		announceOfflineAndStop();
	}

	/** Send UNB1-off while the service is still in the foreground, then stop. */
	private void announceOfflineAndStop() {
		if (discoveryBeacon != null) {
			discoveryBeacon.notifyServerOffline(collectClientIps());
		}
		stopSelf();
	}

	public interface ClientEventSink {
		void onUsbIpEvent(String message);
	}

	private static volatile ClientEventSink eventSink;

	public static void setEventSink(ClientEventSink sink) {
		eventSink = sink;
	}
	
	public void onDestroy() {
		super.onDestroy();
		
		isRunning = false;
		sharedCount = 0;
		sharingClientName = "";
		sharingClientIp = "";

		if (discoveryBeacon != null) {
			discoveryBeacon.stop(collectClientIps());
			discoveryBeacon = null;
		}
		
		if (server != null) {
			server.stop();
		}
		try {
			unregisterReceiver(usbReceiver);
		} catch (Exception ignored) {
		}

		if (lowLatencyWifiLock != null && lowLatencyWifiLock.isHeld()) {
			lowLatencyWifiLock.release();
		}
		if (highPerfWifiLock != null && highPerfWifiLock.isHeld()) {
			highPerfWifiLock.release();
		}
		if (cpuWakeLock != null && cpuWakeLock.isHeld()) {
			cpuWakeLock.release();
		}
	}

	private java.util.List<java.net.InetAddress> collectClientIps() {
		java.util.ArrayList<java.net.InetAddress> ips = new java.util.ArrayList<>();
		if (socketMap == null) return ips;
		synchronized (socketMap) {
			for (Socket s : socketMap.keySet()) {
				try {
					java.net.InetAddress ra = s.getInetAddress();
					if (ra != null) ips.add(ra);
				} catch (Exception ignored) {
				}
			}
		}
		return ips;
	}

	private void publishShareState() {
		String ip = null;
		String name = null;
		if (socketMap != null) {
			synchronized (socketMap) {
				for (Socket s : socketMap.keySet()) {
					try {
						java.net.InetAddress ra = s.getInetAddress();
						if (ra == null) continue;
						ip = ra.getHostAddress();
						if (discoveryBeacon != null)
							name = discoveryBeacon.nameFor(ip);
						break;
					} catch (Exception ignored) {
					}
				}
			}
		}
		sharingClientIp = ip != null ? ip : "";
		sharingClientName = name != null ? name : "";
		if (discoveryBeacon != null)
			discoveryBeacon.setShareState(ip, name);
	}
	
	@Override
	public IBinder onBind(Intent intent) {
		// Not currently bindable
		return null;
	}
	
	// Here we're going to enumerate interfaces and endpoints
	// to eliminate possible speeds until we've narrowed it
	// down to only 1 which is our speed real speed. In a typical
	// USB driver, the host controller knows the real speed but
	// we need to derive it without HCI help.
	private final static int FLAG_POSSIBLE_SPEED_LOW = 0x01;
	private final static int FLAG_POSSIBLE_SPEED_FULL = 0x02;
	private final static int FLAG_POSSIBLE_SPEED_HIGH = 0x04;
	private final static int FLAG_POSSIBLE_SPEED_SUPER = 0x08;
	private int detectSpeed(UsbDevice dev, UsbDeviceDescriptor devDesc) {
		int possibleSpeeds = FLAG_POSSIBLE_SPEED_LOW |
				FLAG_POSSIBLE_SPEED_FULL |
				FLAG_POSSIBLE_SPEED_HIGH |
				FLAG_POSSIBLE_SPEED_SUPER;
		
		for (int i = 0; i < dev.getInterfaceCount(); i++) {
			UsbInterface iface = dev.getInterface(i);
			for (int j = 0; j < iface.getEndpointCount(); j++) {
				UsbEndpoint endpoint = iface.getEndpoint(j);
				if ((endpoint.getType() == UsbConstants.USB_ENDPOINT_XFER_BULK) ||
					(endpoint.getType() == UsbConstants.USB_ENDPOINT_XFER_ISOC)) {
					// Low speed devices can't implement bulk or iso endpoints
					possibleSpeeds &= ~FLAG_POSSIBLE_SPEED_LOW;
				}
				
				if (endpoint.getType() == UsbConstants.USB_ENDPOINT_XFER_CONTROL) {
					if (endpoint.getMaxPacketSize() > 8) {
						// Low speed devices can't use control transfer sizes larger than 8 bytes
						possibleSpeeds &= ~FLAG_POSSIBLE_SPEED_LOW;
					}
					if (endpoint.getMaxPacketSize() < 64) {
						// High speed devices can't use control transfer sizes smaller than 64 bytes
						possibleSpeeds &= ~FLAG_POSSIBLE_SPEED_HIGH;
					}
					if (endpoint.getMaxPacketSize() < 512) {
						// Super speed devices can't use control transfer sizes smaller than 512 bytes
						possibleSpeeds &= ~FLAG_POSSIBLE_SPEED_SUPER;
					}
				}
				else if (endpoint.getType() == UsbConstants.USB_ENDPOINT_XFER_INT) {
					if (endpoint.getMaxPacketSize() > 8) {
						// Low speed devices can't use interrupt transfer sizes larger than 8 bytes
						possibleSpeeds &= ~FLAG_POSSIBLE_SPEED_LOW;
					}
					if (endpoint.getMaxPacketSize() > 64) {
						// Full speed devices can't use interrupt transfer sizes larger than 64 bytes
						possibleSpeeds &= ~FLAG_POSSIBLE_SPEED_FULL;
					}
					if (endpoint.getMaxPacketSize() > 512) {
						// High speed devices can't use interrupt transfer sizes larger than 512 bytes
						possibleSpeeds &= ~FLAG_POSSIBLE_SPEED_HIGH;
					}
				}
				else if (endpoint.getType() == UsbConstants.USB_ENDPOINT_XFER_BULK) {
					// A bulk endpoint alone can accurately distiniguish between
					// full, high, and super speed devices
					switch (endpoint.getMaxPacketSize()) {
						case 512:
							// High speed devices can only use 512 byte bulk transfers
							possibleSpeeds = FLAG_POSSIBLE_SPEED_HIGH;
							break;
						case 1024:
							// Super speed devices can only use 1024 byte bulk transfers
							possibleSpeeds = FLAG_POSSIBLE_SPEED_SUPER;
							break;
						default:
							// Otherwise it must be full speed
							possibleSpeeds = FLAG_POSSIBLE_SPEED_FULL;
							break;
					}
				}
			}
		}
		
		if (devDesc != null) {
			if (devDesc.bcdUSB < 0x200) {
				// High speed only supported on USB 2.0 or higher
				possibleSpeeds &= ~FLAG_POSSIBLE_SPEED_HIGH;
			}
			if (devDesc.bcdUSB < 0x300) {
				// Super speed only supported on USB 3.0 or higher
				possibleSpeeds &= ~FLAG_POSSIBLE_SPEED_SUPER;
			}
		}
		
		// Return the lowest speed that we're compatible with
		if (DEBUG) {
			System.out.printf("Speed heuristics for device %d left us with 0x%x\n",
					dev.getDeviceId(), possibleSpeeds);
		}

		if ((possibleSpeeds & FLAG_POSSIBLE_SPEED_LOW) != 0) {
			return UsbIpDevice.USB_SPEED_LOW;
		}
		else if ((possibleSpeeds & FLAG_POSSIBLE_SPEED_FULL) != 0) {
			return UsbIpDevice.USB_SPEED_FULL;
		}
		else if ((possibleSpeeds & FLAG_POSSIBLE_SPEED_HIGH) != 0) {
			return UsbIpDevice.USB_SPEED_HIGH;
		}
		else if ((possibleSpeeds & FLAG_POSSIBLE_SPEED_SUPER) != 0) {
			return UsbIpDevice.USB_SPEED_SUPER;
		}
		else {
			// Something went very wrong in speed detection
			return UsbIpDevice.USB_SPEED_UNKNOWN;
		}
	}
	
	private static String friendlyDeviceLabel(UsbDevice dev) {
		String product = dev.getProductName();
		String manufacturer = dev.getManufacturerName();
		if (product != null) product = product.trim();
		if (manufacturer != null) manufacturer = manufacturer.trim();
		boolean hasProduct = product != null && !product.isEmpty();
		boolean hasMfr = manufacturer != null && !manufacturer.isEmpty();
		if (hasProduct && hasMfr && !product.toLowerCase(java.util.Locale.US)
				.contains(manufacturer.toLowerCase(java.util.Locale.US))) {
			return manufacturer + " " + product;
		}
		if (hasProduct) return product;
		if (hasMfr) return manufacturer;
		String path = dev.getDeviceName();
		return path != null ? path : "USB device";
	}

	private static int deviceIdToBusNum(int deviceId) {
		return deviceId / 1000;
	}
	
	private static int deviceIdToDevNum(int deviceId) {
		return deviceId % 1000;
	}
	
	private static int devIdToDeviceId(int devId) {
		// This is the same algorithm as Android uses
		return ((devId >> 16) & 0xFF) * 1000 + (devId & 0xFF);
	}
	
	private static int busIdToBusNum(String busId) {
		if (busId.indexOf('-') == -1) {
			return -1;
		}
		
		return Integer.parseInt(busId.substring(0, busId.indexOf('-')));
	}
	
	private static int busIdToDevNum(String busId) {
		if (busId.indexOf('-') == -1) {
			return -1;
		}
		
		return Integer.parseInt(busId.substring(busId.indexOf('-')+1));
	}
	
	private static int busIdToDeviceId(String busId) {
		return devIdToDeviceId(((busIdToBusNum(busId) << 16) & 0xFF0000) | busIdToDevNum(busId));
	}

	private UsbDeviceInfo getInfoForDevice(UsbDevice dev, UsbDeviceConnection devConn) {
		UsbDeviceInfo info = new UsbDeviceInfo();
		UsbIpDevice ipDev = new UsbIpDevice();
		
		ipDev.path = friendlyDeviceLabel(dev);
		ipDev.busnum = deviceIdToBusNum(dev.getDeviceId());
		ipDev.devnum =  deviceIdToDevNum(dev.getDeviceId());
		ipDev.busid = String.format("%d-%d", ipDev.busnum, ipDev.devnum);
		
		ipDev.idVendor = (short) dev.getVendorId();
		ipDev.idProduct = (short) dev.getProductId();
		ipDev.bcdDevice = -1;
		
		ipDev.bDeviceClass = (byte) dev.getDeviceClass();
		ipDev.bDeviceSubClass = (byte) dev.getDeviceSubclass();
		ipDev.bDeviceProtocol = (byte) dev.getDeviceProtocol();

		ipDev.bConfigurationValue = (byte) (dev.getConfigurationCount() > 0 ?
				dev.getConfiguration(0).getId() : 0);
		ipDev.bNumConfigurations = (byte) dev.getConfigurationCount();

		ipDev.bNumInterfaces = (byte) dev.getInterfaceCount();
		
		if (DEBUG) {
			System.out.printf("getInfoForDevice: vid=%04x pid=%04x configValue=%d numConfigs=%d numIfaces=%d\n",
					dev.getVendorId(), dev.getProductId(), ipDev.bConfigurationValue, ipDev.bNumConfigurations, ipDev.bNumInterfaces);
		}
		
		info.dev = ipDev;
		info.interfaces = new UsbIpInterface[ipDev.bNumInterfaces];
		
		for (int i = 0; i < ipDev.bNumInterfaces; i++) {
			info.interfaces[i] = new UsbIpInterface();
			UsbInterface iface = dev.getInterface(i);
			
			info.interfaces[i].bInterfaceClass = (byte) iface.getInterfaceClass();
			info.interfaces[i].bInterfaceSubClass = (byte) iface.getInterfaceSubclass();
			info.interfaces[i].bInterfaceProtocol = (byte) iface.getInterfaceProtocol();
		}
		
		AttachedDeviceContext context = connections.get(dev.getDeviceId());
		UsbDeviceDescriptor devDesc = null;
		if (context != null) {
			// Since we're attached already, we can directly query the USB descriptors
			// to fill some information that Android's USB API doesn't expose
			devDesc = UsbControlHelper.readDeviceDescriptor(context.devConn);
			if (devDesc != null) {
				ipDev.bcdDevice = devDesc.bcdDevice;
			}
		}
		
		ipDev.speed = detectSpeed(dev, devDesc);
		
		return info;
	}
	
	@Override
	public List<UsbDeviceInfo> getDevices() {
		ArrayList<UsbDeviceInfo> list = new ArrayList<>();
		
		for (UsbDevice dev : usbManager.getDeviceList().values()) {
			if (recentlyUnpluggedIds.contains(dev.getDeviceId()))
				continue;
			AttachedDeviceContext context = connections.get(dev.getDeviceId());
			UsbDeviceConnection devConn = null;
			if (context != null) {
				devConn = context.devConn;
			}
			
			list.add(getInfoForDevice(dev, devConn));
		}
		
		return list;
	}
	
	public static void dumpInterfaces(UsbDevice dev) {
		for (int i = 0; i < dev.getInterfaceCount(); i++) {
			System.out.printf("%d - Iface %d (%02x/%02x/%02x)\n",
					i, dev.getInterface(i).getId(),
					dev.getInterface(i).getInterfaceClass(),
					dev.getInterface(i).getInterfaceSubclass(),
					dev.getInterface(i).getInterfaceProtocol());
			
			UsbInterface iface = dev.getInterface(i);
			for (int j = 0; j < iface.getEndpointCount(); j++) {
				System.out.printf("\t%d - Endpoint %d (%x/%x)\n",
						j, iface.getEndpoint(j).getEndpointNumber(),
						iface.getEndpoint(j).getAddress(),
						iface.getEndpoint(j).getAttributes());
			}
		}
	}
	
	private static void sendReply(Socket s, UsbIpSubmitUrbReply reply, int status) {
		reply.status = status;
		try {
			// We need to synchronize to avoid writing on top of ourselves
			synchronized (s) {
				s.getOutputStream().write(reply.serialize());
			}
		} catch (IOException e) {
			e.printStackTrace();
		}
	}
	
	private static void sendReply(Socket s, UsbIpUnlinkUrbReply reply, int status) {
		reply.status = status;
		try {
			// We need to synchronize to avoid writing on top of ourselves
			synchronized (s) {
				s.getOutputStream().write(reply.serialize());
			}
		} catch (IOException e) {
			e.printStackTrace();
		}
	}
	
	// FIXME: This dispatching could use some refactoring so we don't have to pass
	// a million parameters to this guy
	private void dispatchRequest(final AttachedDeviceContext context, final int deviceId, final Socket s,
			final UsbEndpoint selectedEndpoint, final ByteBuffer buff, final UsbIpSubmitUrb msg) {
		context.requestPool.submit(new Runnable() {
			@Override
			public void run() {
				UsbIpSubmitUrbReply reply = new UsbIpSubmitUrbReply(msg.seqNum,
						msg.devId, msg.direction, msg.ep);
				
				if (msg.direction == UsbIpDevicePacket.USBIP_DIR_IN) {
					// We need to store our buffer in the URB reply
					reply.inData = buff.array();
				}
				
				if (selectedEndpoint.getType() == UsbConstants.USB_ENDPOINT_XFER_BULK) {
					if (DEBUG) {
						System.out.printf("Bulk %s EP 0x%02x, %d bytes, seq=%d\n",
								msg.direction == UsbIpDevicePacket.USBIP_DIR_IN ? "IN" : "OUT",
								selectedEndpoint.getAddress(), buff.array().length, msg.seqNum);
					}
					
					int res;
					do {
						res = XferUtils.doBulkTransfer(context.devConn, selectedEndpoint, buff.array(), 1000);
						
						if (res == -110) {
							if (DEBUG) {
								System.out.printf("Bulk %s EP 0x%02x timeout, retrying (seq=%d)\n",
										msg.direction == UsbIpDevicePacket.USBIP_DIR_IN ? "IN" : "OUT",
										selectedEndpoint.getAddress(), msg.seqNum);
							}
						}
						
						if (context.requestPool.isShutdown()) {
							// Bail if the queue is being torn down
							return;
						}
						
						if (!context.activeMessages.contains(msg)) {
							// Somebody cancelled the URB, return without responding
							return;
						}
					} while (res == -110); // ETIMEDOUT
					
					if (DEBUG) {
						System.out.printf("Bulk %s EP 0x%02x complete: res=%d (wanted %d), seq=%d\n",
								msg.direction == UsbIpDevicePacket.USBIP_DIR_IN ? "IN" : "OUT",
								selectedEndpoint.getAddress(), res, msg.transferBufferLength, msg.seqNum);
					}

					if (!context.activeMessages.remove(msg)) {
						// Somebody cancelled the URB, return without responding
						return;
					}
					
					if (res < 0) {
						// Do NOT kill the TCP session on transfer errors — Windows will keep a
						// ghost "attached" device while the phone drops sharing (desync).
						reply.status = res;
						if (res == -19) {
							System.err.printf("Bulk ENODEV on EP %d (deviceId=%d) — replying error, keeping session\n",
									selectedEndpoint.getEndpointNumber(), deviceId);
						}
					}
					else {
						reply.actualLength = res;
						reply.status = ProtoDefs.ST_OK;
					}

					sendReply(s, reply, reply.status);
				}
				else if (selectedEndpoint.getType() == UsbConstants.USB_ENDPOINT_XFER_INT) {
					if (DEBUG) {
						System.out.printf("Interrupt transfer - %d bytes %s on EP %d (maxPacket=%d)\n",
								msg.transferBufferLength, msg.direction == UsbIpDevicePacket.USBIP_DIR_IN ? "in" : "out",
										selectedEndpoint.getEndpointNumber(),
										selectedEndpoint.getMaxPacketSize());
					}
					
					// Windows sometimes submits interrupt URBs larger than maxPacketSize.
					// Oversize USBDEVFS transfers often error and the client disconnects
					// after a brief successful attach (classic "mouse works 1 second" bug).
					byte[] xferBuf = buff.array();
					final int maxPkt = Math.max(1, selectedEndpoint.getMaxPacketSize());
					byte[] ioctlBuf = xferBuf;
					if (xferBuf.length > maxPkt) {
						ioctlBuf = new byte[maxPkt];
						if (msg.direction != UsbIpDevicePacket.USBIP_DIR_IN) {
							System.arraycopy(xferBuf, 0, ioctlBuf, 0, maxPkt);
						}
					}

					// Hold interrupt URBs until data or cancel (normal HID). Control URBs are
					// synchronous on the reader thread, so they are not starved by this.
					final int xferTimeoutMs = 1000;
					int res;
					do {
						res = XferUtils.doInterruptTransfer(context.devConn, selectedEndpoint, ioctlBuf, xferTimeoutMs);

						if (context.requestPool.isShutdown()) {
							return;
						}

						if (!context.activeMessages.contains(msg)) {
							return;
						}
					} while (res == -110); // ETIMEDOUT — keep polling

					if (!context.activeMessages.remove(msg)) {
						return;
					}

					if (res < 0) {
						reply.status = res;
						if (res == -19) {
							appLog("Interrupt ENODEV on EP " + selectedEndpoint.getEndpointNumber());
						}
					}
					else {
						if (ioctlBuf != xferBuf && res > 0) {
							System.arraycopy(ioctlBuf, 0, xferBuf, 0, Math.min(res, xferBuf.length));
						}
						reply.actualLength = Math.min(res, xferBuf.length);
						reply.status = ProtoDefs.ST_OK;
					}

					sendReply(s, reply, reply.status);
				}
				else {
					// Don't kill the whole session for isoc/etc. — reply error so HID can keep working.
					System.err.println("Unsupported endpoint type: "+selectedEndpoint.getType());
					context.activeMessages.remove(msg);
					sendReply(s, reply, ProtoDefs.ST_NA);
				}
			}
		});
	}

	@Override
	public void submitUrbRequest(Socket s, UsbIpSubmitUrb msg) {
		UsbIpSubmitUrbReply reply = new UsbIpSubmitUrbReply(msg.seqNum,
				msg.devId, msg.direction, msg.ep);
		
		int deviceId = devIdToDeviceId(msg.devId);
		
		AttachedDeviceContext context = connections.get(deviceId);
		if (context == null) {
			// Do NOT kill the TCP session — that produces "Read failed: -1" on the reader
			// and leaves Windows with a ghost attach. Reply NA and keep listening.
			appLog("URB for unknown deviceId=" + deviceId + " (devid=0x" +
					Integer.toHexString(msg.devId) + ") ep=" + msg.ep + " — replying NA");
			sendReply(s, reply, ProtoDefs.ST_NA);
			return;
		}
		
		UsbDeviceConnection devConn = context.devConn;
		
		// Control endpoint is handled with a special case
		if (msg.ep == 0) {
			// This is little endian
			ByteBuffer bb = ByteBuffer.wrap(msg.setup).order(ByteOrder.LITTLE_ENDIAN);
			
			final byte requestType = bb.get();
			final byte request = bb.get();
			final short value = bb.getShort();
			final short index = bb.getShort();
			final short length = bb.getShort();
			final boolean controlIn = (requestType & 0x80) != 0;

			// Only IN control transfers carry a reply data stage over USB/IP.
			if (controlIn && length != 0) {
				reply.inData = new byte[length & 0xFFFF];
			}

			context.activeMessages.add(msg);

			// Control MUST stay synchronous on the reader thread. Async + DiscardPolicy
			// (or a pool stuck in interrupt polls) drops SET_CONFIGURATION / GET_DESCRIPTOR
			// replies and Windows closes the socket within ~1s.
			int res;
			if (!UsbControlHelper.handleInternalControlTransfer(context, requestType, request, value, index)) {
				// USB/IP "interval" is frames, not ms — never use it as a timeout.
				final int timeout = 5000;
				byte[] dataBuf;
				if (controlIn) {
					dataBuf = reply.inData;
				} else {
					dataBuf = msg.outData;
					if ((length & 0xFFFF) > 0 && (dataBuf == null || dataBuf.length < (length & 0xFFFF))) {
						appLog("CTRL OUT missing data (setup len=" + (length & 0xFFFF) +
								", got=" + (dataBuf == null ? -1 : dataBuf.length) + ")");
					}
				}
				if (DEBUG) {
					appLog(String.format("CTRL %02x %02x val=%04x idx=%04x len=%d",
							requestType & 0xFF, request & 0xFF, value & 0xFFFF, index & 0xFFFF, length & 0xFFFF));
				}
				res = XferUtils.doControlTransfer(devConn, requestType, request, value, index,
						dataBuf, length, timeout);
			}
			else {
				if (DEBUG) {
					appLog(String.format("CTRL handled internally %02x %02x val=%04x",
							requestType & 0xFF, request & 0xFF, value & 0xFFFF));
				}
				res = 0;
			}

			if (!context.activeMessages.remove(msg)) {
				return;
			}

			if (res < 0) {
				appLog("Control failed: " + res);
				reply.status = res;
				reply.actualLength = 0;
			}
			else {
				// For OUT, actualLength is the count but the buffer must NOT be sent on the wire.
				reply.actualLength = controlIn
						? Math.min(res, reply.inData != null ? reply.inData.length : 0)
						: res;
				reply.status = ProtoDefs.ST_OK;
				if (DEBUG) {
					appLog("Control OK actual=" + reply.actualLength + (controlIn ? " IN" : " OUT"));
				}
			}

			sendReply(s, reply, reply.status);
		}
		else {
			// Find the correct endpoint
			UsbEndpoint selectedEndpoint = null;
			if (context.activeConfigurationEndpointsByNumDir != null) {
				int endptNumDir = msg.ep + (msg.direction == UsbIpDevicePacket.USBIP_DIR_IN ? UsbConstants.USB_DIR_IN : 0);
				selectedEndpoint = context.activeConfigurationEndpointsByNumDir.get(endptNumDir);
			}
			else {
				System.err.println("Attempted to transfer to non-control EP before SET_CONFIGURATION!");
			}
			
			if (selectedEndpoint == null) {
				appLog("EP not found: " + msg.ep + " (config not set yet?)");
				sendReply(s, reply, ProtoDefs.ST_NA);
				return;
			}
			
			ByteBuffer buff;
			if (msg.direction == UsbIpDevicePacket.USBIP_DIR_IN) {
				// The buffer is allocated by us
				buff = ByteBuffer.allocate(msg.transferBufferLength);
			}
			else {
				// The buffer came in with the request
				buff = ByteBuffer.wrap(msg.outData);
			}
			
			// This message is now active
			context.activeMessages.add(msg);
			
			// Dispatch this request asynchronously
			dispatchRequest(context, deviceId, s, selectedEndpoint, buff, msg);
		}
	}
	
	private UsbDevice getDevice(int deviceId) {
		if (recentlyUnpluggedIds.contains(deviceId))
			return null;
		for (UsbDevice dev : usbManager.getDeviceList().values()) {
			if (dev.getDeviceId() == deviceId) {
				return dev;
			}
		}
		
		return null;
	}
	
	private UsbDevice getDevice(String busId) {
		return getDevice(busIdToDeviceId(busId));
	}

	@Override
	public UsbDeviceInfo getDeviceByBusId(String busId) {
		UsbDevice dev = getDevice(busId);
		if (dev == null) {
			return null;
		}
		
		AttachedDeviceContext context = connections.get(dev.getDeviceId());
		UsbDeviceConnection devConn = null;
		if (context != null) {
			devConn = context.devConn;
		}
		
		return getInfoForDevice(dev, devConn);
	}

	@Override
	public boolean attachToDevice(Socket s, String busId) {
		UsbDevice dev = getDevice(busId);
		if (dev == null) {
			appLog("attachToDevice: device not found for busId " + busId);
			return false;
		}

		appLog(String.format("attachToDevice: %s id=%d vid=%04x pid=%04x",
				busId, dev.getDeviceId(), dev.getVendorId(), dev.getProductId()));

		if (connections.get(dev.getDeviceId()) != null) {
			appLog("attachToDevice: already attached");
			return false;
		}

		if (!usbManager.hasPermission(dev)) {
			appLog("attachToDevice: requesting USB permission — tap Allow on phone");
			int deviceId = dev.getDeviceId();
			permission.put(deviceId, null);
			usbManager.requestPermission(dev, usbPermissionIntent);
			synchronized (permission) {
				long deadline = System.currentTimeMillis() + 30000;
				while (permission.get(deviceId) == null) {
					long remaining = deadline - System.currentTimeMillis();
					if (remaining <= 0) {
						appLog("attachToDevice: permission timed out");
						return false;
					}
					try {
						permission.wait(remaining);
					} catch (InterruptedException e) {
						return false;
					}
				}
			}

			if (!permission.get(deviceId)) {
				appLog("attachToDevice: permission denied");
				return false;
			}
			appLog("attachToDevice: permission granted");
		}

		UsbDeviceConnection devConn = usbManager.openDevice(dev);
		if (devConn == null) {
			appLog("attachToDevice: openDevice failed");
			return false;
		}

		AttachedDeviceContext context = new AttachedDeviceContext();
		context.devConn = devConn;
		context.device = dev;
		context.activeMessages = java.util.concurrent.ConcurrentHashMap.newKeySet();

		// Claim interfaces so HID class requests (SET_REPORT etc.) work even if
		// Windows skips SET_CONFIGURATION because we advertised a config value.
		if (dev.getConfigurationCount() > 0) {
			UsbConfiguration config = dev.getConfiguration(0);
			try {
				devConn.setConfiguration(config);
			} catch (Exception ignored) {
			}
			context.activeConfiguration = config;
			context.activeConfigurationEndpointsByNumDir = new SparseArray<>();
			for (int i = 0; i < config.getInterfaceCount(); i++) {
				UsbInterface iface = config.getInterface(i);
				try {
					org.cgutman.usbip.jni.UsbLib.disconnectKernelDriver(
							devConn.getFileDescriptor(), iface.getId());
				} catch (Exception ignored) {
				}
				boolean claimed = devConn.claimInterface(iface, true);
				appLog("Claim iface " + iface.getId() + ": " + (claimed ? "OK" : "FAIL"));
				for (int j = 0; j < iface.getEndpointCount(); j++) {
					UsbEndpoint endp = iface.getEndpoint(j);
					context.activeConfigurationEndpointsByNumDir.put(
							endp.getDirection() | endp.getEndpointNumber(), endp);
				}
			}
		}

		int endpointCount = 0;
		for (int i = 0; i < dev.getInterfaceCount(); i++) {
			endpointCount += dev.getInterface(i).getEndpointCount();
		}
		// Never use DiscardPolicy — dropped URB replies make Windows close the socket.
		int poolSize = Math.max(8, endpointCount * 2 + 4);
		context.requestPool = new ThreadPoolExecutor(poolSize, poolSize,
				Long.MAX_VALUE, TimeUnit.DAYS,
				new LinkedBlockingQueue<>());

		connections.put(dev.getDeviceId(), context);
		activeDeviceIds.add(dev.getDeviceId());
		socketMap.put(s, context);

		appLog("attachToDevice: opened, waiting for Windows SET_CONFIGURATION / URBs");
		updateNotification();
		return true;
	}
	
	private void cleanupDetachedDevice(int deviceId) {
		cleanupDetachedDevice(deviceId, null, false);
	}

	private void cleanupDetachedDevice(int deviceId, UsbDevice detachedHint, boolean notifyGone) {
		AttachedDeviceContext context = connections.get(deviceId);

		UsbDevice usbForMeta = context != null ? context.device : detachedHint;
		if (usbForMeta == null)
			usbForMeta = getDevice(deviceId);

		String busId = null;
		String vid = null;
		String pid = null;
		if (usbForMeta != null) {
			busId = String.format("%d-%d",
					deviceIdToBusNum(usbForMeta.getDeviceId()),
					deviceIdToDevNum(usbForMeta.getDeviceId()));
			vid = String.format("%04x", usbForMeta.getVendorId());
			pid = String.format("%04x", usbForMeta.getProductId());
		}

		// Close Windows import sockets for this device so the PC drops the ghost port.
		java.util.ArrayList<Socket> doomed = new java.util.ArrayList<>();
		synchronized (socketMap) {
			for (java.util.Map.Entry<Socket, AttachedDeviceContext> e : socketMap.entrySet()) {
				AttachedDeviceContext ctx = e.getValue();
				if (ctx != null && ctx.device != null && ctx.device.getDeviceId() == deviceId) {
					doomed.add(e.getKey());
				}
			}
			for (Socket s : doomed) {
				socketMap.remove(s);
			}
		}

		java.util.ArrayList<java.net.InetAddress> clientIps = new java.util.ArrayList<>();
		for (Socket s : doomed) {
			try {
				java.net.InetAddress ra = s.getInetAddress();
				if (ra != null) clientIps.add(ra);
			} catch (Exception ignored) {
			}
			try {
				s.shutdownOutput();
			} catch (Exception ignored) {
			}
			try {
				s.close();
			} catch (Exception ignored) {
			}
			appLog("Closed client socket after USB unplug (deviceId=" + deviceId + ")");
		}

		// Only for physical USB unplug — not when Windows closes the TCP session.
		if (notifyGone && busId != null && discoveryBeacon != null) {
			discoveryBeacon.notifyDeviceGone(busId, vid != null ? vid : "0000",
					pid != null ? pid : "0000", clientIps);
		}

		if (context == null) {
			return;
		}
		
		// Clear the this attachment's context
		connections.remove(deviceId);
		activeDeviceIds.remove(deviceId);
		
		// Signal queue death
		context.requestPool.shutdownNow();
		
		// Release our claim to the interfaces
		for (int i = 0; i < context.device.getInterfaceCount(); i++) {
			context.devConn.releaseInterface(context.device.getInterface(i));
		}

		// Close the connection
		context.devConn.close();
		
		// Wait for the queue to die (capped so UI thread never freezes on slow workers)
		try {
			context.requestPool.awaitTermination(1500, TimeUnit.MILLISECONDS);
		} catch (InterruptedException e) {}

		updateNotification();
	}
	
	@Override
	public void detachFromDevice(Socket s, String busId) {
		UsbDevice dev = getDevice(busId);
		if (dev == null) {
			return;
		}
		
		cleanupDetachedDevice(dev.getDeviceId(), null, false);
	}

	@Override
	public void cleanupSocket(Socket s) {
		AttachedDeviceContext context = socketMap.remove(s);
		if (context == null) {
			return;
		}
		
		cleanupDetachedDevice(context.device.getDeviceId(), null, false);
	}

	@Override
	public void abortUrbRequest(Socket s, UsbIpUnlinkUrb msg) {
		AttachedDeviceContext context = socketMap.get(s);
		if (context == null) {
			return;
		}
		
		UsbIpUnlinkUrbReply reply = new UsbIpUnlinkUrbReply(msg.seqNum, msg.devId, msg.direction, msg.ep);
		
		boolean found = false;
		synchronized (context.activeMessages) {
			for (UsbIpSubmitUrb urbMsg : context.activeMessages) {
				if (msg.seqNumToUnlink == urbMsg.seqNum) {
					context.activeMessages.remove(urbMsg);
					found = true;
					break;
				}
			}
		}
		
		if (DEBUG) {
			System.out.println("Removed URB? " + (found ? "yes" : "no"));
		}
		sendReply(s, reply,
				found ? -104 /* ECONNRESET: Linux/Windows expect this for successful unlink */ :
					-22); // EINVAL
	}

}
