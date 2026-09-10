package com.usbnetbridge.server

import java.net.Inet4Address
import java.net.NetworkInterface
import java.util.Collections
import java.util.Locale

/** Local IPv4 addresses with a simple Wi‑Fi / VPN / cellular label for the UI. */
internal data class LanAddress(
    val ip: String,
    val kind: Kind,
    val ifaceName: String,
) {
    enum class Kind(val rank: Int, val label: String) {
        Wifi(0, "Wi‑Fi"),
        Ethernet(1, "Ethernet"),
        Vpn(2, "VPN"),
        Cellular(3, "Cellular"),
        Other(4, "Other"),
    }
}

internal object LanAddresses {
    fun list(): List<LanAddress> {
        val found = mutableListOf<LanAddress>()
        try {
            for (nif in Collections.list(NetworkInterface.getNetworkInterfaces())) {
                if (!nif.isUp || nif.isLoopback) continue
                val kind = classify(nif.name)
                // Cellular IPs are not reachable for USB/IP (CGNAT) and confuse Find phone.
                if (kind == LanAddress.Kind.Cellular) continue
                for (addr in Collections.list(nif.inetAddresses)) {
                    if (addr !is Inet4Address) continue
                    if (addr.isLoopbackAddress || addr.isLinkLocalAddress) continue
                    val ip = addr.hostAddress ?: continue
                    if (isCarrierGradeNat(ip)) continue
                    found += LanAddress(ip, kind, nif.name)
                }
            }
        } catch (_: Exception) {
        }
        return found.distinctBy { it.ip }.sortedWith(
            compareBy<LanAddress> { it.kind.rank }
                .thenBy { privateRank(it.ip) }
                .thenBy { it.ip }
        )
    }

    fun primaryIp(): String? = list().firstOrNull()?.ip

    private fun classify(rawName: String): LanAddress.Kind {
        val name = rawName.lowercase(Locale.US)
        return when {
            name.startsWith("wlan") || name.startsWith("wifi") || name.startsWith("ap") ->
                LanAddress.Kind.Wifi
            name.startsWith("eth") ->
                LanAddress.Kind.Ethernet
            name.startsWith("tun") || name.startsWith("tap") || name.startsWith("wg") ||
                name.startsWith("ppp") || name.contains("vpn") || name.startsWith("tunl") ->
                LanAddress.Kind.Vpn
            name.startsWith("rmnet") || name.startsWith("ccmni") || name.startsWith("radio") ||
                name.contains("mobile") || name.startsWith("wwan") ||
                name.startsWith("usb0") || name.startsWith("v4-rmnet") ->
                LanAddress.Kind.Cellular
            else -> LanAddress.Kind.Other
        }
    }

    /** Carrier CGNAT (100.64.0.0/10) — typical cell data, not usable from a PC. */
    private fun isCarrierGradeNat(ip: String): Boolean {
        val parts = ip.split('.')
        if (parts.size != 4) return false
        val a = parts[0].toIntOrNull() ?: return false
        val b = parts[1].toIntOrNull() ?: return false
        return a == 100 && b in 64..127
    }

    private fun privateRank(ip: String): Int = when {
        ip.startsWith("192.168.") -> 0
        ip.startsWith("10.") -> 1
        ip.startsWith("172.") -> 2
        else -> 3
    }
}
