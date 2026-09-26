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

        // Mutex check bypassed

        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => { System.IO.File.WriteAllText("crash.txt", e.Exception.ToString()); };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { System.IO.File.WriteAllText("crash.txt", e.ExceptionObject.ToString()); };
            
            using var form = new MainForm(
                Environment.GetCommandLineArgs().Any(a =>
                    string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase)));
            Application.Run(form);
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText("crash.txt", ex.ToString());
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
