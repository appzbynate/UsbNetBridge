package com.usbnetbridge.server

import java.text.SimpleDateFormat
import java.util.ArrayDeque
import java.util.Date
import java.util.Locale
import java.util.concurrent.CopyOnWriteArrayList

/**
 * In-app log that does not depend on USB adb/logcat.
 * Service + Activity both write here so events survive even if the UI was paused.
 */
object AppLog {
    private const val MAX = 200
    private val lines = ArrayDeque<String>(MAX)
    private val listeners = CopyOnWriteArrayList<(String) -> Unit>()
    private val timeFmt = SimpleDateFormat("HH:mm:ss", Locale.US)

    @Volatile
    var lastLine: String = "(no events yet)"
        private set

    @Synchronized
    fun append(message: String) {
        val line = "${timeFmt.format(Date())}  $message"
        if (lines.size >= MAX) lines.removeFirst()
        lines.addLast(line)
        lastLine = line
        for (l in listeners) {
            try {
                l(line)
            } catch (_: Exception) {
            }
        }
    }

    @Synchronized
    fun snapshot(): String {
        val s = lines.joinToString("\n")
        return if (s.isBlank()) "(no events yet)" else s
    }

    fun addListener(listener: (String) -> Unit) {
        listeners.add(listener)
    }

    fun removeListener(listener: (String) -> Unit) {
        listeners.remove(listener)
    }
}
