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
            binding.logView.append(line + "\n")
            binding.logView.setSelection(binding.logView.text?.length ?: 0)
            updateServerUi()
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
        binding.copyLogButton.setOnClickListener { copyLogToClipboard() }

        ensureNotificationPermission()
        UsbIpService.setEventSink {
            runOnUiThread { updateServerUi() }
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
        binding.logView.setText(AppLog.snapshot())
        binding.logView.setSelection(binding.logView.text?.length ?: 0)
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
        return usbManager.deviceList.isNotEmpty()
    }

    private fun refreshDevices() {
        val usbManager = getSystemService(USB_SERVICE) as UsbManager
        val devices = usbManager.deviceList.values.toList().sortedBy { it.deviceName }
        if (devices.isEmpty()) {
            binding.deviceListText.text = getString(R.string.devices_empty)
            updateServerUi()
            return
        }
        binding.deviceListText.text = devices.joinToString("\n\n") { d ->
            val product = d.productName?.trim().orEmpty()
            val manufacturer = d.manufacturerName?.trim().orEmpty()
            val name = when {
                product.isNotEmpty() && manufacturer.isNotEmpty() &&
                    !product.contains(manufacturer, ignoreCase = true) -> "$manufacturer $product"
                product.isNotEmpty() -> product
                manufacturer.isNotEmpty() -> manufacturer
                else -> "USB device"
            }
            val busnum = d.deviceId / 1000
            val devnum = d.deviceId % 1000
            val busid = "$busnum-$devnum"
            val perm = if (usbManager.hasPermission(d)) "OK" else "ask on attach"
            val onPc = UsbIpService.getSharingClientName()
            val using = if (UsbIpService.isRunning && UsbIpService.getSharedDeviceCount() > 0 && onPc.isNotBlank())
                getString(R.string.device_on_pc, onPc)
            else ""
            String.format(
                Locale.US,
                "%s\n(%04x:%04x)  [%s]  perm=%s%s",
                name,
                d.vendorId,
                d.productId,
                busid,
                perm,
                using
            )
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
        val (label, colorRes) = when {
            !running -> getString(R.string.status_stopped) to R.color.status_stopped
            shared > 0 && pcName.isNotBlank() -> getString(R.string.status_on_pc, pcName) to R.color.status_sharing
            shared > 0 -> getString(R.string.status_sharing, shared) to R.color.status_sharing
            else -> getString(R.string.status_waiting) to R.color.status_waiting
        }
        binding.statusText.text = label
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

    private fun copyLogToClipboard() {
        val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        clipboard.setPrimaryClip(ClipData.newPlainText("UsbNetBridge log", AppLog.snapshot()))
        toast("Log copied")
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
