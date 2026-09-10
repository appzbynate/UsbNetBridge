package com.usbnetbridge.server

import android.content.Context
import android.net.Uri
import android.net.wifi.WifiManager
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.SocketTimeoutException
import java.nio.charset.StandardCharsets
import java.util.concurrent.atomic.AtomicBoolean

/**
 * LAN discovery for UsbNetBridge.
 * Windows clients broadcast [PROBE]; we reply with [REPLY_PREFIX]ip|usbipPort|name|dev=...
 * Also beacons periodically so passive listeners can find us.
 */
class DiscoveryBeacon(
    private val context: Context,
    private val usbIpPort: Int,
    private val onLog: (String) -> Unit = {},
) {
    private val running = AtomicBoolean(false)
    private var thread: Thread? = null
    private var socket: DatagramSocket? = null
    private var multicastLock: WifiManager.MulticastLock? = null
    private val recentClientIps = LinkedHashSet<InetAddress>()
    private val clientNames = HashMap<String, String>()
    @Volatile private var shareClientIp: String? = null
    @Volatile private var shareClientName: String? = null
    @Volatile private var usbSummaryCache: String = ""

    fun start() {
        refreshUsbCache()
        if (!running.compareAndSet(false, true)) return
        thread = Thread({
            try {
                acquireMulticastLock()
                val sock = DatagramSocket(DISCOVERY_PORT).apply {
                    broadcast = true
                    reuseAddress = true
                    soTimeout = 1000
                }
                socket = sock
                onLog("LAN discovery listening on UDP $DISCOVERY_PORT")
                var lastBeaconMs = 0L
                val buf = ByteArray(1024)
                while (running.get()) {
                    // Periodic beacon
                    val now = System.currentTimeMillis()
                    if (now - lastBeaconMs >= beaconIntervalMs()) {
                        broadcastAnnounce(sock)
                        lastBeaconMs = now
                    }
                    try {
                        val packet = DatagramPacket(buf, buf.size)
                        sock.receive(packet)
                        val text = String(packet.data, packet.offset, packet.length, StandardCharsets.UTF_8).trim()
                        if (text.startsWith(PROBE)) {
                            rememberClient(packet.address, parseProbePcName(text))
                            replyTo(sock, packet.address, packet.port)
                        }
                    } catch (_: SocketTimeoutException) {
                        // loop for beacon / stop check
                    } catch (e: Exception) {
                        if (running.get()) {
                            onLog("Discovery receive: ${e.message}")
                        }
                    }
                }
            } catch (e: Exception) {
                onLog("Discovery failed to start: ${e.message}")
            } finally {
                try {
                    socket?.close()
                } catch (_: Exception) {
                }
                socket = null
                releaseMulticastLock()
            }
        }, "usbnetbridge-discovery").also { it.isDaemon = true; it.start() }
    }

    fun stop() {
        stop(emptyList())
    }

    fun stop(clientIps: Collection<InetAddress>) {
        // Tell Windows immediately — before the listen socket / multicast lock go away.
        notifyServerOffline(clientIps)
        running.set(false)
        try {
            socket?.close()
        } catch (_: Exception) {
        }
        thread?.interrupt()
        thread = null
    }

    private fun replyTo(sock: DatagramSocket, to: InetAddress, port: Int) {
        val payload = buildAnnounce().toByteArray(StandardCharsets.UTF_8)
        try {
            sock.send(DatagramPacket(payload, payload.size, to, port))
        } catch (e: Exception) {
            onLog("Discovery reply failed: ${e.message}")
        }
    }

    private fun broadcastAnnounce(sock: DatagramSocket) {
        val payload = buildAnnounce().toByteArray(StandardCharsets.UTF_8)
        try {
            sock.send(
                DatagramPacket(
                    payload,
                    payload.size,
                    InetAddress.getByName("255.255.255.255"),
                    DISCOVERY_PORT
                )
            )
        } catch (_: Exception) {
            // ignore beacon failures (common on some OEMs)
        }
    }

    /**
     * Tell Windows clients immediately that a USB device was unplugged.
     * Message: UNB1-gone|phoneIp|busid:vid:pid  → UDP [EVENT_PORT]
     */
    fun notifyDeviceGone(
        busId: String,
        vid: String,
        pid: String,
        clientIps: Collection<InetAddress> = emptyList(),
    ) {
        val ip = primaryIpv4() ?: "0.0.0.0"
        val payload = "$GONE_PREFIX$ip|$busId:$vid:$pid".toByteArray(StandardCharsets.UTF_8)
        onLog("Notifying Windows: device gone [$busId] $vid:$pid")
        Thread({
            sendEvent(payload, clientIps, "Device-gone notify failed")
            // Last USB device is gone — Android will stop the server. Tell Windows now,
            // on this same path (the one that already works for unplug).
            refreshUsbCache()
            if (usbSummaryCache.isEmpty()) {
                notifyServerOffline(clientIps)
            } else {
                try {
                    socket?.let { broadcastAnnounce(it) }
                } catch (_: Exception) {
                }
            }
        }, "usbnetbridge-gone").apply { isDaemon = true; start() }
    }

    /**
     * Tell Windows the USB/IP server is stopping.
     * Message: UNB1-off|ip[,ip…]  → UDP [EVENT_PORT]
     * Sent on a background thread (same as device-gone) so the USB/main thread
     * cannot stall the packet; we join briefly so Stop still waits for it.
     */
    fun notifyServerOffline(clientIps: Collection<InetAddress> = emptyList()) {
        val ips = LanAddresses.list().map { it.ip }.ifEmpty {
            listOf(primaryIpv4() ?: "0.0.0.0")
        }
        val payload = "$OFFLINE_PREFIX${ips.joinToString(",")}".toByteArray(StandardCharsets.UTF_8)
        onLog("Notifying Windows: server offline (${ips.joinToString(", ")})")
        val t = Thread({
            sendEvent(payload, clientIps, "Server-offline notify failed")
        }, "usbnetbridge-off")
        t.isDaemon = true
        t.start()
        try {
            t.join(500)
        } catch (_: Exception) {
        }
    }

    fun setShareState(clientIp: String?, clientName: String?) {
        shareClientIp = clientIp
        shareClientName = clientName?.ifBlank { null }
    }

    fun nameFor(ip: String?): String? {
        if (ip.isNullOrBlank()) return null
        synchronized(clientNames) {
            return clientNames[ip]
        }
    }

    /** Re-read UsbManager. Call on plug/unplug — not from the beacon loop. */
    fun refreshUsbCache() {
        usbSummaryCache = computePluggedUsbSummary()
    }

    private fun beaconIntervalMs(): Long =
        if (shareClientIp.isNullOrBlank()) BEACON_INTERVAL_MS else BEACON_WHILE_SHARING_MS

    private fun buildAnnounce(): String {
        val ip = primaryIpv4() ?: "0.0.0.0"
        val devices = usbSummaryCache
        val fields = ArrayList<String>()
        fields.add("$REPLY_PREFIX$ip")
        fields.add("$usbIpPort")
        fields.add("UsbNetBridge")
        fields.add("dev=$devices")
        batteryField()?.let { fields.add(it) }
        wifiField()?.let { fields.add(it) }
        val busyIp = shareClientIp
        val busyPc = shareClientName
        if (!busyIp.isNullOrBlank()) {
            fields.add("busy=${Uri.encode(busyIp)}")
            if (!busyPc.isNullOrBlank())
                fields.add("pc=${Uri.encode(busyPc)}")
        }
        return fields.joinToString("|")
    }

    private fun computePluggedUsbSummary(): String {
        return try {
            val usb = context.applicationContext
                .getSystemService(Context.USB_SERVICE) as? android.hardware.usb.UsbManager
                ?: return ""
            usb.deviceList.values.map { d ->
                val busnum = d.deviceId / 1000
                val devnum = d.deviceId % 1000
                val busid = "$busnum-$devnum"
                val vid = String.format("%04x", d.vendorId)
                val pid = String.format("%04x", d.productId)
                val label = friendlyUsbName(d)
                val enc = Uri.encode(label) ?: label
                val cls = usbClassHint(d)
                "$busid:$vid:$pid:$enc:$cls"
            }.joinToString(",")
        } catch (_: Exception) {
            ""
        }
    }

    private fun usbClassHint(d: android.hardware.usb.UsbDevice): String {
        val classes = ArrayList<Int>()
        classes.add(d.deviceClass)
        try {
            for (i in 0 until d.interfaceCount)
                classes.add(d.getInterface(i).interfaceClass)
        } catch (_: Exception) {
        }
        return when {
            classes.any { it == android.hardware.usb.UsbConstants.USB_CLASS_HID } -> "hid"
            classes.any { it == android.hardware.usb.UsbConstants.USB_CLASS_AUDIO ||
                it == 14 /* USB_CLASS_VIDEO */ } -> "iso"
            classes.any { it == android.hardware.usb.UsbConstants.USB_CLASS_MASS_STORAGE } -> "stor"
            classes.any { it == android.hardware.usb.UsbConstants.USB_CLASS_HUB } -> "hub"
            else -> "other"
        }
    }

    private fun batteryField(): String? {
        return try {
            val intent = context.registerReceiver(null, android.content.IntentFilter(android.content.Intent.ACTION_BATTERY_CHANGED))
                ?: return null
            val level = intent.getIntExtra(android.os.BatteryManager.EXTRA_LEVEL, -1)
            val scale = intent.getIntExtra(android.os.BatteryManager.EXTRA_SCALE, 100).coerceAtLeast(1)
            if (level < 0) return null
            val pct = ((level * 100f) / scale).toInt().coerceIn(0, 100)
            val status = intent.getIntExtra(android.os.BatteryManager.EXTRA_STATUS, -1)
            val charging = status == android.os.BatteryManager.BATTERY_STATUS_CHARGING ||
                status == android.os.BatteryManager.BATTERY_STATUS_FULL
            if (charging) "bat=${pct}c" else "bat=$pct"
        } catch (_: Exception) {
            null
        }
    }

    private fun wifiField(): String? {
        return try {
            val wm = context.applicationContext.getSystemService(Context.WIFI_SERVICE) as? WifiManager
                ?: return null
            @Suppress("DEPRECATION")
            val info = wm.connectionInfo ?: return null
            val rssi = info.rssi
            if (rssi >= 0 || rssi <= -127) return null
            val quality = ((rssi + 100).coerceIn(0, 50) * 2)
            "wifi=$quality"
        } catch (_: Exception) {
            null
        }
    }

    private fun parseProbePcName(text: String): String? {
        val rest = text.removePrefix(PROBE).trim().trimStart('|')
        if (!rest.startsWith("pc=", ignoreCase = true)) return null
        val raw = rest.substring(3).substringBefore('|').trim()
        if (raw.isEmpty()) return null
        return try {
            Uri.decode(raw)
        } catch (_: Exception) {
            raw
        }
    }

    private fun friendlyUsbName(d: android.hardware.usb.UsbDevice): String {
        val product = d.productName?.trim().orEmpty()
        val manufacturer = d.manufacturerName?.trim().orEmpty()
        return when {
            product.isNotEmpty() && manufacturer.isNotEmpty() &&
                !product.contains(manufacturer, ignoreCase = true) -> "$manufacturer $product"
            product.isNotEmpty() -> product
            manufacturer.isNotEmpty() -> manufacturer
            else -> "USB device"
        }
    }

    private fun primaryIpv4(): String? = LanAddresses.primaryIp()

    private fun rememberClient(addr: InetAddress?, pcName: String? = null) {
        if (addr == null || addr.isLoopbackAddress || addr.isAnyLocalAddress) return
        synchronized(recentClientIps) {
            recentClientIps.add(addr)
            while (recentClientIps.size > 12) {
                val first = recentClientIps.first()
                recentClientIps.remove(first)
            }
        }
        val ip = addr.hostAddress ?: return
        val name = pcName?.trim().orEmpty()
        if (name.isNotEmpty()) {
            synchronized(clientNames) {
                clientNames[ip] = name
            }
        }
    }

    private fun sendEvent(
        payload: ByteArray,
        extraClients: Collection<InetAddress>,
        failLog: String,
    ) {
        try {
            DatagramSocket().use { sock ->
                sock.broadcast = true
                val seen = HashSet<String>()
                val targets = ArrayList<InetAddress>()
                synchronized(recentClientIps) {
                    targets.addAll(recentClientIps)
                }
                targets.addAll(extraClients)
                val ports = intArrayOf(EVENT_PORT, DISCOVERY_PORT)
                repeat(3) {
                    for (port in ports) {
                        try {
                            sock.send(
                                DatagramPacket(
                                    payload,
                                    payload.size,
                                    InetAddress.getByName("255.255.255.255"),
                                    port
                                )
                            )
                        } catch (_: Exception) {
                        }
                    }
                    for (addr in targets) {
                        val key = addr.hostAddress ?: continue
                        if (!seen.add(key)) continue
                        if (addr.isLoopbackAddress || addr.isAnyLocalAddress) continue
                        rememberClient(addr)
                        for (port in ports) {
                            try {
                                sock.send(DatagramPacket(payload, payload.size, addr, port))
                            } catch (_: Exception) {
                            }
                        }
                    }
                    seen.clear()
                }
            }
        } catch (e: Exception) {
            onLog("$failLog: ${e.message}")
        }
    }

    private fun acquireMulticastLock() {
        try {
            val wm = context.applicationContext.getSystemService(Context.WIFI_SERVICE) as? WifiManager
                ?: return
            multicastLock = wm.createMulticastLock("UsbNetBridge:Discovery").apply {
                setReferenceCounted(false)
                acquire()
            }
        } catch (_: Exception) {
        }
    }

    private fun releaseMulticastLock() {
        try {
            if (multicastLock?.isHeld == true) multicastLock?.release()
        } catch (_: Exception) {
        }
        multicastLock = null
    }

    companion object {
        const val DISCOVERY_PORT = 3241
        /** Windows clients listen here for unplug / state push events. */
        const val EVENT_PORT = 3242
        const val PROBE = "UNB1?"
        const val REPLY_PREFIX = "UNB1!"
        const val GONE_PREFIX = "UNB1-gone|"
        const val OFFLINE_PREFIX = "UNB1-off|"
        private const val BEACON_INTERVAL_MS = 3000L
        private const val BEACON_WHILE_SHARING_MS = 15000L
    }
}
