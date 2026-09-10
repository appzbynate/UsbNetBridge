using System.Runtime.InteropServices;

namespace UsbNetBridge.Client;

internal static class Program
{
    internal const string ShowWindowMessage = "UsbNetBridge.Client.ShowWindow";
    private const string MutexName = @"Local\UsbNetBridge.Client.Instance";
    private const string ShowEventName = @"Local\UsbNetBridge.Client.Show";

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var mutex = new Mutex(false, MutexName);
        bool owned;
        try
        {
            owned = mutex.WaitOne(TimeSpan.Zero, false);
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }

        using var showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!owned)
        {
            NotifyRunningInstance(showRequested);
            return;
        }

        try
        {
            using var form = new MainForm(
                Environment.GetCommandLineArgs().Any(a =>
                    string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase)));
            form.ListenForActivation(showRequested);
            Application.Run(form);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static void NotifyRunningInstance(EventWaitHandle showRequested)
    {
        try
        {
            var msg = Native.RegisterWindowMessage(ShowWindowMessage);
            var hwnd = Native.FindWindow(null, "UsbNetBridge");
            if (hwnd != IntPtr.Zero)
            {
                Native.GetWindowThreadProcessId(hwnd, out var pid);
                if (pid != 0)
                    Native.AllowSetForegroundWindow(unchecked((int)pid));
                Native.PostMessage(hwnd, msg, IntPtr.Zero, IntPtr.Zero);
            }
            else
            {
                Native.AllowSetForegroundWindow(-1);
            }
        }
        catch
        {
            // Named event still wakes the running instance.
        }

        showRequested.Set();
    }

    private static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool AllowSetForegroundWindow(int dwProcessId);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
