package com.usbnetbridge.server

import android.Manifest
import android.app.PendingIntent
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.drawable.GradientDrawable
import android.hardware.usb.UsbDevice
import android.hardware.usb.UsbManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.provider.Settings
import android.view.LayoutInflater
import android.view.View
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import androidx.core.graphics.drawable.DrawableCompat
import com.usbnetbridge.server.databinding.ActivityMainBinding
import org.cgutman.usbip.server.UsbIpServer
import org.cgutman.usbip.service.UsbIpService
import java.util.Locale

/** Control panel for the USB/IP foreground service. */
class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private val mainHandler = Handler(Looper.getMainLooper())
    private var startingServer = false

    private val logListener: (String) -> Unit = { line ->
        runOnUiThread {
            binding.lastEventText.text = line
        }
    }

    private val refreshRunnable = object : Runnable {
        override fun run() {
            updateServerUi()
            // Don't walk UsbManager while HID URBs are in flight — it stalls the mouse ~1s.
            if (UsbIpService.getSharedDeviceCount() == 0)
                refreshDevices()
            binding.lastEventText.text = AppLog.lastLine
            mainHandler.postDelayed(this, if (UsbIpService.getSharedDeviceCount() > 0) 4000 else 1500)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        binding.startButton.setOnClickListener { startUsbIpServer() }
        binding.stopButton.setOnClickListener { stopUsbIpServer() }
        binding.refreshButton.setOnClickListener { refreshDevices() }
        binding.copyEndpointButton.setOnClickListener { copyEndpointToClipboard() }
        binding.showLogsButton.setOnClickListener { showLogsDialog() }

        ensureNotificationPermission()
        UsbIpService.setEventSink {
            runOnUiThread {
                updateServerUi()
                refreshDevices()
            }
        }

        AppLog.addListener(logListener)
        reloadLogFromBuffer()
        updateServerUi()
        refreshDevices()
        handleUsbAttachIntent(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleUsbAttachIntent(intent)
    }

    override fun onDestroy() {
        AppLog.removeListener(logListener)
        UsbIpService.setEventSink(null)
        super.onDestroy()
    }

    override fun onResume() {
        super.onResume()
        reloadLogFromBuffer()
        updateServerUi()
        refreshDevices()
        mainHandler.post(refreshRunnable)
    }

    override fun onPause() {
        mainHandler.removeCallbacks(refreshRunnable)
        super.onPause()
    }

    /**
     * System launches us with USB_DEVICE_ATTACHED when the user picks this app
     * (and “Always” remembers that choice for the device). Open the UI only —
     * Start is always a manual tap so Stop cannot be undone by USB re-enumeration.
     */
    private fun handleUsbAttachIntent(intent: Intent?) {
        if (intent?.action != UsbManager.ACTION_USB_DEVICE_ATTACHED) return

        val device: UsbDevice? = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            intent.getParcelableExtra(UsbManager.EXTRA_DEVICE, UsbDevice::class.java)
        } else {
            @Suppress("DEPRECATION")
            intent.getParcelableExtra(UsbManager.EXTRA_DEVICE)
        }

        val name = device?.productName ?: device?.deviceName ?: "USB device"
        AppLog.append("USB plugged in: $name — tap Start to share")
        toast(getString(R.string.usb_attached_toast))
        refreshDevices()

        if (device != null) {
            maybeRequestUsbPermission(device)
        }

        // Consume the attach intent so rotate / USB re-plug after Stop does not re-run this.
        setIntent(Intent(this, MainActivity::class.java))
    }

    private fun maybeRequestUsbPermission(device: UsbDevice) {
        val usbManager = getSystemService(USB_SERVICE) as UsbManager
        if (usbManager.hasPermission(device)) {
            refreshDevices()
            return
        }

        val flags = PendingIntent.FLAG_UPDATE_CURRENT or
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) PendingIntent.FLAG_MUTABLE else 0
        val pi = PendingIntent.getBroadcast(
            this,
            device.deviceId,
            Intent(ACTION_USB_PERMISSION).setPackage(packageName),
            flags
        )
        AppLog.append(getString(R.string.usb_permission_hint))
        usbManager.requestPermission(device, pi)
    }

    private fun reloadLogFromBuffer() {
        binding.lastEventText.text = AppLog.lastLine.ifBlank { "—" }
    }

    private fun startUsbIpServer() {
        if (!hasPluggedUsbDevices()) {
            AppLog.append(getString(R.string.need_usb_to_start))
            toast(getString(R.string.need_usb_to_start))
            updateServerUi()
            return
        }
        startingServer = true
        binding.startButton.isEnabled = false
        maybeRequestUnrestrictedBattery()
        ContextCompat.startForegroundService(this, Intent(this, UsbIpService::class.java))
        AppLog.append("Starting USB/IP on port ${UsbIpServer.PORT}")
        mainHandler.postDelayed({
            if (!UsbIpService.isRunning) startingServer = false
            updateServerUi()
        }, 800)
    }

    private fun stopUsbIpServer() {
        startingServer = false
        if (UsbIpService.isRunning) {
            // Ask the running service to announce UNB1-off while still in the foreground.
            startService(Intent(this, UsbIpService::class.java).setAction(UsbIpService.ACTION_STOP))
        } else {
            stopService(Intent(this, UsbIpService::class.java))
        }
        AppLog.append("Stop requested")
        mainHandler.postDelayed({ updateServerUi() }, 300)
    }

    private fun hasPluggedUsbDevices(): Boolean {
        val usbManager = getSystemService(USB_SERVICE) as UsbManager
        return usbManager.deviceList.values.any { !UsbIpService.isRecentlyUnplugged(it.deviceId) }
    }

    private fun refreshDevices() {
        val usbManager = getSystemService(USB_SERVICE) as UsbManager
        val devices = usbManager.deviceList.values
            .filter { !UsbIpService.isRecentlyUnplugged(it.deviceId) }
            .sortedBy { it.deviceName }
        if (devices.isEmpty()) {
            binding.deviceEmptyLayout.visibility = View.VISIBLE
            binding.deviceListText.text = getString(R.string.empty_usb_detail)
            binding.devicesContainer.visibility = View.GONE
            binding.devicesContainer.removeAllViews()
            updateServerUi()
            return
        }

        binding.deviceEmptyLayout.visibility = View.GONE
        binding.devicesContainer.visibility = View.VISIBLE
        binding.devicesContainer.removeAllViews()

        val inflater = LayoutInflater.from(this)
        val onPc = UsbIpService.getSharingClientName()
        val isSharing = UsbIpService.isRunning && UsbIpService.getSharedDeviceCount() > 0 && onPc.isNotBlank()

        for (d in devices) {
            val itemView = inflater.inflate(R.layout.item_usb_device, binding.devicesContainer, false)
            val name = DiscoveryBeacon.friendlyUsbName(d)
            val busnum = d.deviceId / 1000
            val devnum = d.deviceId % 1000
            val busid = "$busnum-$devnum"
            val hasPerm = usbManager.hasPermission(d)

            val nameView = itemView.findViewById<TextView>(R.id.deviceName)
            val detailsView = itemView.findViewById<TextView>(R.id.deviceDetails)
            val badgeView = itemView.findViewById<TextView>(R.id.deviceStatusBadge)

            nameView.text = name
            detailsView.text = String.format(Locale.US, "%04x:%04x  •  Bus [%s]", d.vendorId, d.productId, busid)

            when {
                UsbIpService.activeDeviceIds.contains(d.deviceId) -> {
                    if (onPc.isNotBlank()) {
                        badgeView.text = getString(R.string.badge_sharing, onPc)
                    } else {
                        badgeView.text = getString(R.string.badge_sharing, "PC")
                    }
                    badgeView.setBackgroundResource(R.drawable.bg_badge_info)
                    badgeView.setTextColor(ContextCompat.getColor(this, R.color.accent))
                }
                hasPerm -> {
                    badgeView.text = getString(R.string.badge_ready)
                    badgeView.setBackgroundResource(R.drawable.bg_badge_ok)
                    badgeView.setTextColor(ContextCompat.getColor(this, R.color.ok))
                }
                else -> {
                    badgeView.text = getString(R.string.badge_need_perm)
                    badgeView.setBackgroundResource(R.drawable.bg_badge_warn)
                    badgeView.setTextColor(ContextCompat.getColor(this, R.color.warning))
                    itemView.setOnClickListener {
                        maybeRequestUsbPermission(d)
                    }
                }
            }
            binding.devicesContainer.addView(itemView)
        }
        updateServerUi()
    }

    private fun updateServerUi() {
        val running = UsbIpService.isRunning
        if (running) startingServer = false
        val hasUsb = hasPluggedUsbDevices()
        binding.startButton.isEnabled = !running && !startingServer && hasUsb
        binding.stopButton.isEnabled = running

        val shared = if (running) UsbIpService.getSharedDeviceCount() else 0
        val pcName = UsbIpService.getSharingClientName()
        val (label, sub, colorRes) = when {
            !running -> Triple(getString(R.string.status_stopped), getString(R.string.status_stopped_sub), R.color.status_stopped)
            shared > 0 && pcName.isNotBlank() -> Triple(getString(R.string.status_on_pc, pcName), getString(R.string.status_sharing_sub, pcName), R.color.status_sharing)
            shared > 0 -> Triple(getString(R.string.status_sharing, shared), getString(R.string.status_sharing_sub, "PC"), R.color.status_sharing)
            else -> Triple(getString(R.string.status_waiting), getString(R.string.status_waiting_sub), R.color.status_waiting)
        }
        binding.statusText.text = label
        binding.statusSubtitle.text = sub
        setStatusDot(ContextCompat.getColor(this, colorRes))
        updateEndpointUi(running)
    }

    private fun setStatusDot(color: Int) {
        val d = binding.statusDot.background?.mutate() ?: return
        if (d is GradientDrawable) {
            d.setColor(color)
        } else {
            DrawableCompat.setTint(DrawableCompat.wrap(d), color)
        }
        binding.statusDot.background = d
    }

    private fun updateEndpointUi(running: Boolean) {
        val port = UsbIpServer.PORT
        val addrs = LanAddresses.list()
        when {
            !running -> {
                binding.endpointText.text = getString(R.string.endpoint_idle)
                binding.endpointHintText.text = getString(R.string.hint_screen)
                binding.copyEndpointButton.isEnabled = false
                binding.copyEndpointButton.tag = null
            }
            addrs.isEmpty() -> {
                binding.endpointText.text = getString(R.string.endpoint_no_ip)
                binding.endpointHintText.text = getString(R.string.hint_screen)
                binding.copyEndpointButton.isEnabled = false
                binding.copyEndpointButton.tag = null
            }
            else -> {
                val primary = addrs.first()
                val endpoint = "${primary.ip}:$port"
                binding.endpointText.text = addrs.joinToString("\n") { addr ->
                    val mark = if (addrs.size > 1 && addr.ip == primary.ip) "  ← use this" else ""
                    "${addr.kind.label}  ${addr.ip}:$port$mark"
                }
                binding.endpointHintText.text = if (UsbIpService.getSharedDeviceCount() > 0 &&
                    UsbIpService.getSharingClientName().isNotBlank()) {
                    getString(R.string.status_on_pc, UsbIpService.getSharingClientName())
                } else if (addrs.size > 1) {
                    getString(R.string.hint_pick_ip)
                } else {
                    getString(R.string.hint_screen)
                }
                binding.copyEndpointButton.isEnabled = true
                binding.copyEndpointButton.tag = endpoint
            }
        }
    }

    private fun copyEndpointToClipboard() {
        val value = binding.copyEndpointButton.tag as? String ?: return
        val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        clipboard.setPrimaryClip(ClipData.newPlainText("UsbNetBridge", value))
        toast("Copied $value")
    }

    private fun showLogsDialog() {
        val builder = android.app.AlertDialog.Builder(this)
        val ctx = builder.context
        val scrollView = android.widget.ScrollView(ctx)
        val textView = android.widget.TextView(ctx).apply {
            text = AppLog.snapshot()
            textSize = 12f
            typeface = android.graphics.Typeface.MONOSPACE
            setTextColor(android.graphics.Color.parseColor("#111827"))
            setPadding(32, 32, 32, 32)
        }
        scrollView.addView(textView)

        builder
            .setTitle("Activity Logs")
            .setView(scrollView)
            .setPositiveButton("COPY") { _, _ ->
                val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
                clipboard.setPrimaryClip(android.content.ClipData.newPlainText("UsbNetBridge log", AppLog.snapshot()))
                toast("Log copied")
            }
            .setNegativeButton("CLOSE", null)
            .show()
    }


    private fun toast(msg: String) =
        Toast.makeText(this, msg, Toast.LENGTH_SHORT).show()

    private fun ensureNotificationPermission() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) return
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS)
            == PackageManager.PERMISSION_GRANTED
        ) return
        ActivityCompat.requestPermissions(
            this,
            arrayOf(Manifest.permission.POST_NOTIFICATIONS),
            1001
        )
    }

    private fun maybeRequestUnrestrictedBattery() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.M) return
        val pm = getSystemService(PowerManager::class.java) ?: return
        if (pm.isIgnoringBatteryOptimizations(packageName)) return
        try {
            startActivity(
                Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS).apply {
                    data = Uri.parse("package:$packageName")
                }
            )
            AppLog.append("In App info → Battery, set usage to Unrestricted.")
            toast("Set Battery to Unrestricted for reliable sharing")
        } catch (_: Exception) {
            AppLog.append("Set this app’s battery usage to Unrestricted in Settings.")
        }
    }

    companion object {
        /** Must match UsbIpService so Allow/Deny is recorded there. */
        private const val ACTION_USB_PERMISSION = "org.cgutman.usbip.USB_PERMISSION"
    }
}
