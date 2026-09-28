using System.Drawing.Drawing2D;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.Http;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace UsbNetBridge.Client;

/// <summary>Beginner-friendly USB passthrough client UI.</summary>
public sealed class MainForm : Form
{
    private const string AppTitle = "UsbNetBridge";
    private const int CornerRadius = 22;
    private const int WellRadius = 18;
    private const int ButtonHeight = 40;
    private const int ActiveItemHeight = 38;
    private const int ActiveVisibleLines = 2;
    // Section title + card chrome + list rows + Disconnect row (shown when a device is attached).
    private static int ActiveSectionHeight =>
        44 + 24 + 8 + (ActiveVisibleLines * ActiveItemHeight) + 96 + 20;
    private const int InventoryTopMinHeight = 280;
    private const int LogSectionMinHeight = 140;
    private const int HeaderSectionHeight = 44;
    private const int ConnectSectionEstimate = 100;

    // Icon-inspired palette (cyan / indigo / soft night blue)
    private static readonly Color BgDeep = UiTheme.BgDeep;
    private static readonly Color BgMid = UiTheme.BgMid;
    private static readonly Color CardFace = UiTheme.CardFace;
    private static readonly Color CardFaceLite = UiTheme.CardFaceLite;
    private static readonly Color Accent = UiTheme.Accent;
    private static readonly Color AccentHot = UiTheme.AccentHot;
    private static readonly Color TextPrimary = UiTheme.TextPrimary;
    private static readonly Color TextMuted = UiTheme.TextMuted;
    private static readonly Color OkGreen = UiTheme.OkGreen;
    private static readonly Color Danger = UiTheme.Danger;
    private static readonly Color InputBg = UiTheme.InputBg;
    private static readonly Color CardBorder = UiTheme.CardBorder;

    private UsbipCli? _cli;

    private readonly TextBox _hostBox = new();
    private SoftTextWell? _hostWell;
    private readonly Soft3dButton _findBtn = new();
    private readonly Soft3dButton _refreshBtn = new();
    private readonly ConnectingHero _connectHero = new();
    private readonly LinkLabel _manualToggle = new();
    private FlowLayoutPanel? _manualRow;
    private FlowLayoutPanel? _savedRow;
    private readonly ComboBox _savedHostsBox = new();
    private readonly Soft3dButton _saveHostBtn = new();
    private readonly Soft3dButton _removeHostBtn = new();
    private readonly List<string> _savedHosts = new();
    private bool _updatingSavedHostsUi;
    private bool _manualAddressVisible;
    private readonly SoftListBox _serverList = new();
    private readonly SoftListBox _deviceList = new();
    private readonly SoftListBox _attachedList = new();
    private readonly SoftEmptyState _serversEmpty = new();
    private readonly SoftEmptyState _devicesEmpty = new();
    private readonly SoftEmptyState _attachedEmpty = new();
    private readonly Soft3dButton _clearLogBtn = new();
    private readonly SoftLogView _logBox = new();
    private Panel? _updateBanner;

    private readonly NotifyIcon _tray;
    private readonly SemaphoreSlim _findLock = new(1, 1);
    private readonly Dictionary<string, OnlineServer> _onlineServers = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    /// <summary>Hosts that sent UNB1-off — ignore in-flight beacons for a moment.</summary>
    private readonly Dictionary<string, DateTime> _stoppedHostsUntil = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan StoppedHostGrace = TimeSpan.FromSeconds(2.5);
    /// <summary>Phone product labels keyed by busid and by vid:pid (from discovery).</summary>
    private readonly Dictionary<string, string> _phoneLabelsByBusId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _phoneLabelsByVidPid = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _phoneClassByBusId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _phoneClassByVidPid = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<AttachedUsbDevice> _attachedCache = Array.Empty<AttachedUsbDevice>();
    private IReadOnlyList<RemoteUsbDevice> _remoteCache = Array.Empty<RemoteUsbDevice>();
    private AutoConnectPref? _autoConnect;
    private readonly HashSet<string> _autoConnectHoldoff = new(StringComparer.OrdinalIgnoreCase);
    private bool _autoConnectBusy;
    private ContextMenuStrip? _autoConnectMenu;
    private CancellationTokenSource? _phoneWatchCts;
    private System.Windows.Forms.Timer? _portPollTimer;
    private System.Windows.Forms.Timer? _inventoryTimer;
    private System.Windows.Forms.Timer? _searchPulseTimer;
    private System.Windows.Forms.Timer? _pingTimer;
    private int _searchDotPhase;
    private bool _pingBusy;
    private int _livenessMisses;
    private PhoneEventListener? _phoneEvents;
    private bool _portPollBusy;
    private bool _inventoryBusy;
    private bool _detachStaleBusy;
    private DateTime _suppressGoneUntilUtc = DateTime.MinValue;
    private DateTime _offlineHandledUntilUtc = DateTime.MinValue;
    private readonly Dictionary<string, DateTime> _goneUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _offlineGate = new();
    private bool _forceExit;
    private bool _exiting;
    private bool _mockupMode;
    private bool _closePromptOpen;
    private bool _alwaysMinimizeToTray;
    private ToolStripMenuItem? _alwaysTrayMenuItem;
    private ToolStripMenuItem? _startWithWindowsItem;
    private readonly bool _startInTray;
    private EventWaitHandle? _showRequested;
    private bool _trayTipShown;
    private bool _autoFindStarted;
    private bool _loggedWaitingForPhone;
    private Image? _circuitBackground;
    private Bitmap? _windowBgCache;
    private Size _windowBgCacheSize;
    private Control? _connectCard;
    private Form? _logForm;

    private void ShowLogWindow()
    {
        if (_logForm != null && !_logForm.IsDisposed)
        {
            if (_logForm.WindowState == FormWindowState.Minimized)
                _logForm.WindowState = FormWindowState.Normal;
            _logForm.BringToFront();
            return;
        }

        _logForm = new Form
        {
            Text = "Activity Logs - UsbNetBridge",
            Size = new Size(600, 400),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = UiTheme.BgDeep,
            ForeColor = UiTheme.TextPrimary,
            ShowIcon = false
        };
        
        var logBoxWrapper = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        _logBox.Dock = DockStyle.Fill;
        logBoxWrapper.Controls.Add(_logBox);
        _logForm.Controls.Add(logBoxWrapper);

        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(12) };
        var copyBtn = new Soft3dButton { Text = "Copy Logs", Size = new Size(120, 36), Dock = DockStyle.Right };
        StylePrimaryButton(copyBtn, "Copy Logs");
        copyBtn.Click += (_, _) => {
            var text = _logBox.GetFullText();
            if (!string.IsNullOrWhiteSpace(text))
                Clipboard.SetText(text);
        };
        bottomPanel.Controls.Add(copyBtn);
        
        var clearBtn = new Soft3dButton { Text = "Clear", Size = new Size(80, 36), Dock = DockStyle.Left };
        StyleSecondaryButton(clearBtn, "Clear");
        clearBtn.Click += (_, _) => _logBox.Clear();
        bottomPanel.Controls.Add(clearBtn);

        _logForm.Controls.Add(bottomPanel);
        _logForm.FormClosed += (_, _) => _logForm = null;
        _logForm.Show(this);
    }
    public MainForm(bool startInTray = false)
    {
        _startInTray = startInTray;
        Text = AppTitle;
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        SizeGripStyle = SizeGripStyle.Hide;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint, true);
        BackColor = BgDeep;
        ForeColor = TextPrimary;
        ShowInTaskbar = true;
        Icon = LoadAppIcon();
        _circuitBackground = LoadCircuitBackground();
        _alwaysMinimizeToTray = LoadAlwaysMinimizeToTray();

        _tray = CreateTrayIcon();
        _tray.Icon = (Icon)Icon.Clone();
        _tray.Visible = true;

        StyleControls();
        BuildLayout();
        UpdateListEmptyVisible(_serverList, _serversEmpty);
        UpdateDevicesEmptyVisible();
        UpdateListEmptyVisible(_attachedList, _attachedEmpty);
        UpdateDisconnectUi();
        ApplyMinWindowSize();
        StartSearchPulse();
        StartLatencyPing();
        StartPortPolling();
        StartInventoryPolling();
        StartPhoneEventListener();
        RefreshDriverStatus();
        LoadSavedHost();
        LoadAutoConnect();
        _ = CheckForUpdatesAsync();

        _findBtn.Click += async (_, _) => await FindPhoneAsync(autoRefresh: true, quiet: false);
        _refreshBtn.Click += async (_, _) => await RefreshDevicesAsync();
        _deviceList.DoubleClick += async (_, _) => await AttachSelectedAsync();
        _deviceList.MouseUp += DeviceListOnMouseUp;
        _serverList.MouseUp += ServerListOnMouseUp;
        _attachedList.SelectedIndexChanged += (_, _) => UpdateDisconnectUi();
        _attachedList.DoubleClick += async (_, _) => await DetachSelectedAsync();
        _attachedList.MouseUp += AttachedListOnMouseUp;
        _serverList.SelectedIndexChanged += (_, _) =>
        {
            if (_serverList.SelectedItem is OnlineServer s)
            {
                _hostBox.Text = s.Host;
                _ = RefreshDevicesAsync(quiet: true);
            }
        };

        Shown += async (_, _) => await OnFirstShownAsync();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle &= ~0x0003; // ~(CS_VREDRAW | CS_HREDRAW)
            cp.Style |= 0x02000000; // WS_CLIPCHILDREN
            cp.Style &= unchecked((int)~0x00050000); // ~(WS_THICKFRAME | WS_MAXIMIZEBOX)
            return cp;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    private const int HotkeyDetachAll = 0x71;
    private const int HotkeyReconnect = 0x72;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;

    private static readonly uint WmShowInstance = RegisterWindowMessage(Program.ShowWindowMessage);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT Reserved;
        public POINT MaxSize;
        public POINT MaxPosition;
        public POINT MinTrackSize;
        public POINT MaxTrackSize;
    }

    protected override void WndProc(ref Message m)
    {
        const int wmErasebkgnd = 0x0014;
        const int wmGetMinMaxInfo = 0x0024;
        const int wmNcHitTest = 0x0084;
        const int wmNcLButtonDblClk = 0x00A3;
        const int wmSysCommand = 0x0112;
        const int scSize = 0xF000;
        const int scMaximize = 0xF030;

        if (m.Msg == wmErasebkgnd)
        {
            m.Result = 1;
            return;
        }

        if ((uint)m.Msg == WmShowInstance)
        {
            RestoreFromTray();
            return;
        }

        if (m.Msg == 0x0312) // WM_HOTKEY
        {
            var id = m.WParam.ToInt32();
            if (id == HotkeyDetachAll)
                _ = DetachAllAsync();
            else if (id == HotkeyReconnect)
                _ = ReconnectHotkeyAsync();
            return;
        }

        // Win11 snap / title-bar double-click / border drag still reach here
        // even with FormBorderStyle.FixedSingle if maximize is left enabled.
        if (m.Msg == wmSysCommand)
        {
            var cmd = m.WParam.ToInt32() & 0xFFF0;
            if (cmd == scSize || cmd == scMaximize)
                return;
        }

        if (m.Msg == wmNcLButtonDblClk)
            return;

        if (m.Msg == wmNcHitTest)
        {
            base.WndProc(ref m);
            var hit = m.Result.ToInt32();
            // HTLEFT..HTBOTTOMRIGHT (10-17): treat as caption so edges move, never resize.
            if (hit is >= 10 and <= 17)
                m.Result = 2; // HTCAPTION
            return;
        }

        if (m.Msg == wmGetMinMaxInfo && !MinimumSize.IsEmpty)
        {
            base.WndProc(ref m);
            var info = Marshal.PtrToStructure<MINMAXINFO>(m.LParam);
            info.MinTrackSize = new POINT { X = MinimumSize.Width, Y = MinimumSize.Height };
            var max = MaximumSize.IsEmpty ? MinimumSize : MaximumSize;
            info.MaxTrackSize = new POINT { X = max.Width, Y = max.Height };
            info.MaxSize = info.MaxTrackSize;
            Marshal.StructureToPtr(info, m.LParam, false);
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyMinWindowSize();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyMinWindowSize();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Win11 DWM resize animation composites child HWNDs mid-layout. Turn it off.
        var disable = 1;
        _ = DwmSetWindowAttribute(Handle, 3 /* DWMWA_TRANSITIONS_FORCEDISABLED */, ref disable, sizeof(int));

        // Enable Windows 10/11 immersive dark mode title bar matching the app theme
        var darkMode = 1;
        _ = DwmSetWindowAttribute(Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref darkMode, sizeof(int));
        _ = DwmSetWindowAttribute(Handle, 19 /* DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 */, ref darkMode, sizeof(int));
        var captionColor = 0x001D0F0A; // COLORREF: 0x00BBGGRR for #0A0F1D
        _ = DwmSetWindowAttribute(Handle, 35 /* DWMWA_CAPTION_COLOR */, ref captionColor, sizeof(int));
        var textColor = 0x00FFFFFF; // Crisp white title text
        _ = DwmSetWindowAttribute(Handle, 36 /* DWMWA_TEXT_COLOR */, ref textColor, sizeof(int));

        var mods = ModControl | ModAlt | ModNoRepeat;
        if (RegisterHotKey(Handle, HotkeyDetachAll, mods, (uint)Keys.D) &&
            RegisterHotKey(Handle, HotkeyReconnect, mods, (uint)Keys.A))
            Log("Hotkeys: Ctrl+Alt+D disconnect all, Ctrl+Alt+A reconnect.");
        else
            Log("Could not register Ctrl+Alt+D / Ctrl+Alt+A (already in use).");
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        InvalidateWindowBackgroundCache();
        base.OnSizeChanged(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Draw here so transparent gutters / header ask the form for this backdrop.
        EnsureWindowBackgroundCache();
        if (_windowBgCache != null)
            e.Graphics.DrawImageUnscaled(_windowBgCache, 0, 0);
        else
            PaintWindowBackground(e.Graphics, ClientRectangle);
    }

    private float UiScale => DeviceDpi > 0 ? DeviceDpi / 96f : 1f;

    private Size ComputeMinClientSize()
    {
        var s = UiScale;
        // Fixed-chrome layout: root fills the window exactly. Sections divide space
        // proportionally via SizeType.Percent. No scrollbar needed.
        // 680 unscaled → 1020px at 150% DPI, fits comfortably on 1080p.
        var h = 680;
        return new Size(
            (int)Math.Ceiling(490 * s),
            (int)Math.Ceiling(h * s));
    }

    private void ApplyMinWindowSize()
    {
        var designed = SizeFromClientSize(ComputeMinClientSize());
        var wa = IsHandleCreated
            ? Screen.FromControl(this).WorkingArea
            : Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, designed.Width, designed.Height);
        var locked = new Size(
            Math.Min(designed.Width, Math.Max(640, wa.Width)),
            Math.Min(designed.Height, Math.Max(480, wa.Height)));
        MaximumSize = Size.Empty;
        MinimumSize = Size.Empty;
        if (WindowState == FormWindowState.Normal)
            Size = locked;
        MinimumSize = locked;
        MaximumSize = locked;
    }

    private void SyncConnectCardWidths()
    {
        if (_connectCard == null)
            return;
        _connectHero.Invalidate();
    }

    private void StyleControls()
    {
        _hostBox.PlaceholderText = "IP address";
        _hostWell = new SoftTextWell(_hostBox)
        {
            Width = 188,
            MinimumSize = new Size(160, 40),
            Margin = new Padding(0, 0, 10, 6),
        };

        StylePrimaryButton(_findBtn, "Find");
        StyleSecondaryButton(_refreshBtn, "Refresh");
        _refreshBtn.Size = new Size(92, 32);
        StyleSecondaryButton(_saveHostBtn, "Save");
        _saveHostBtn.Size = new Size(72, 32);
        StyleSecondaryButton(_removeHostBtn, "Remove");

        _savedHostsBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _savedHostsBox.FlatStyle = FlatStyle.Flat;
        _savedHostsBox.BackColor = InputBg;
        _savedHostsBox.ForeColor = TextPrimary;
        _savedHostsBox.Font = new Font("Segoe UI", 10f);
        _savedHostsBox.Width = 188;
        _savedHostsBox.IntegralHeight = false;
        _savedHostsBox.ItemHeight = 22;
        _savedHostsBox.MinimumSize = new Size(160, 36);
        _savedHostsBox.Height = 36;
        _savedHostsBox.Margin = new Padding(0, 4, 10, 6);
        _savedHostsBox.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingSavedHostsUi) return;
            _removeHostBtn.Enabled = _savedHostsBox.SelectedIndex >= 0;
            if (_savedHostsBox.SelectedItem is string host && !string.IsNullOrWhiteSpace(host))
                _hostBox.Text = host;
        };
        _saveHostBtn.Click += (_, _) => SaveTypedHost();
        _removeHostBtn.Click += (_, _) => RemoveSelectedSavedHost();

        _connectHero.Headline = SearchPulseText(3);
        _connectHero.HeadlineColor = Accent;
        _connectHero.Hint = "Start UsbNetBridge on your device and tap Start — this PC will find it automatically.";

        _manualToggle.AutoSize = true;
        _manualToggle.Text = "Enter address manually";
        _manualToggle.LinkColor = Accent;
        _manualToggle.ActiveLinkColor = AccentHot;
        _manualToggle.VisitedLinkColor = Accent;
        _manualToggle.Font = new Font("Segoe UI Semibold", 9.25f);
        _manualToggle.BackColor = Color.Transparent;
        _manualToggle.Margin = new Padding(0, 2, 0, 4);
        _manualToggle.LinkClicked += (_, _) => ToggleManualAddress();

        StyleSoftList(_serverList);
        StyleSoftList(_deviceList);
        StyleSoftList(_attachedList);

        _serversEmpty.Glyph = SoftEmptyGlyph.None;
        _serversEmpty.SetCopy("No USB hosts found", "");
        _devicesEmpty.Glyph = SoftEmptyGlyph.None;
        _devicesEmpty.SetCopy("No devices yet", "");
        _attachedEmpty.Glyph = SoftEmptyGlyph.None;
        _attachedEmpty.SetCopy("No active devices", "Right-click devices for more options");


        StyleSecondaryButton(_clearLogBtn, "Clear");
        _clearLogBtn.Size = new Size(84, 32);
        _clearLogBtn.Click += (_, _) =>
        {
            _logBox.Clear();
            Log("Activity cleared.");
        };

        _logBox.Dock = DockStyle.Fill;
        _logBox.MinimumSize = new Size(0, 140);
    }

    private void StyleSoftList(SoftListBox list)
    {
        list.Dock = DockStyle.Fill;
        list.ItemHeight = (int)(UiTheme.ListItemHeight * UiScale);
    }

    private void BuildLayout()
    {
        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14),
            BackColor = Color.Transparent,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // update banner
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderSectionHeight * UiScale)); // header
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // connect
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // inventory (fills remaining)

        _updateBanner = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 32,
            Visible = false,
            BackColor = Color.FromArgb(0, 110, 200),
            Margin = new Padding(0, 0, 0, 8),
            Cursor = Cursors.Hand
        };
        var updateLabel = new Label
        {
            Text = "New update available! Click here to download.",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9.5f),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };
        _updateBanner.Controls.Add(updateLabel);
        _updateBanner.Click += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/appzbynate/UsbNetBridge/releases/latest") { UseShellExecute = true });
        updateLabel.Click += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/appzbynate/UsbNetBridge/releases/latest") { UseShellExecute = true });
        root.Controls.Add(_updateBanner, 0, 0);

        var header = new DoubleBufferedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
        };
        var logsBtn = new Soft3dButton { Text = "Logs", Size = new Size(80, 32) };
        StyleSecondaryButton(logsBtn, "Logs");
        logsBtn.Click += (_, _) => ShowLogWindow();
        
        var logsWrapper = new Panel { Dock = DockStyle.Right, Width = 80 + 8 };
        logsBtn.Location = new Point(0, (HeaderSectionHeight - logsBtn.Height) / 2);
        logsWrapper.Controls.Add(logsBtn);
        header.Controls.Add(logsWrapper);

        var logo = new BufferedLogo
        {
            Dock = DockStyle.Fill
        };
        header.Controls.Add(logo);

        root.Controls.Add(header, 0, 1);

        // Auto-sized connect card so titles/buttons never clip / overlap
        var connect = MakeCard();
        connect.AutoSize = true;
        connect.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        connect.Dock = DockStyle.Top;
        connect.Padding = new Padding(8, 8, 8, 8);
        connect.Margin = new Padding(0, 0, 0, 8);

        _connectHero.Dock = DockStyle.Top;
        _connectHero.AutoSize = true;
        _connectHero.Margin = new Padding(0);
        connect.Controls.Add(_connectHero);
        UiTheme.BindRoundRegion(_connectHero, 16);
        _connectCard = connect;
        connect.SizeChanged += (_, _) => SyncConnectCardWidths();
        root.Controls.Add(connect, 0, 2);

        // Inventory: servers/devices on top (flexible), Active fixed for two rows + disconnect.
        // No SplitContainer — avoids startup crashes from invalid splitter distances.
        var mid = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 4, 0, 6),
        };
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mid.RowStyles.Add(new RowStyle(SizeType.Percent, 65));  // topMid (hosts + devices)
        mid.RowStyles.Add(new RowStyle(SizeType.Percent, 35));  // active

        var topMid = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        topMid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topMid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));  // servers card
        topMid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));  // devices card

        _manualRow = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 4),
            Padding = new Padding(0),
            Visible = false,
        };
        var ipLbl = new Label
        {
            Text = "Address",
            AutoSize = true,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI Semibold", 9f),
            Margin = new Padding(0, 12, 8, 0),
            BackColor = Color.Transparent,
        };
        _manualRow.Controls.Add(ipLbl);
        _manualRow.Controls.Add(_hostWell!);
        _manualRow.Controls.Add(PadBtn(_findBtn));

        _savedRow = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
            Visible = false,
        };
        var savedLbl = new Label
        {
            Text = "Saved",
            AutoSize = true,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI Semibold", 9f),
            Margin = new Padding(0, 10, 8, 0),
            BackColor = Color.Transparent,
        };
        _savedRow.Controls.Add(savedLbl);
        _savedRow.Controls.Add(_savedHostsBox);
        _savedRow.Controls.Add(PadBtn(_saveHostBtn));
        _savedRow.Controls.Add(PadBtn(_removeHostBtn));

        var serverFooter = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = CardFace,
            Margin = new Padding(0),
            Padding = new Padding(0, 6, 0, 0),
        };
        serverFooter.Controls.Add(_manualToggle);
        serverFooter.Controls.Add(_manualRow);
        serverFooter.Controls.Add(_savedRow);
        serverFooter.Resize += (_, _) => 
        {
            _manualRow.Width = serverFooter.ClientSize.Width;
            _savedRow.Width = serverFooter.ClientSize.Width;
        };

        var serversCard = MakeSectionCard("USB hosts", _serverList, _serversEmpty, footer: serverFooter);
        serversCard.Margin = new Padding(0, 0, 0, 10);
        var devicesCard = MakeSectionCard("Devices", _deviceList, _devicesEmpty, headerTrailing: _refreshBtn);
        devicesCard.Margin = new Padding(0, 10, 0, 0);
        topMid.Controls.Add(serversCard, 0, 0);
        topMid.Controls.Add(devicesCard, 0, 1);

        var attachedBody = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        attachedBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        attachedBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // list fills remaining

        var attachedHost = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(2) };
        _attachedList.Dock = DockStyle.Fill;
        _attachedEmpty.Dock = DockStyle.Fill;
        attachedHost.Controls.Add(_attachedList);
        attachedHost.Controls.Add(_attachedEmpty);
        _attachedEmpty.BringToFront();


        attachedBody.Controls.Add(attachedHost, 0, 0);

        var attachedCard = MakeSectionCard("Active", attachedBody, null);
        attachedCard.Margin = new Padding(0, 12, 0, 0);

        mid.Controls.Add(topMid, 0, 0);
        mid.Controls.Add(attachedCard, 0, 1);
        root.Controls.Add(mid, 0, 3);

        _logBox.Dock = DockStyle.Fill;
        // removed logCard

        Controls.Add(root);
    }

    private Panel MakeSectionCard(string title, Control body, Control? overlay, Control? headerTrailing = null, Control? footer = null)
    {
        var card = MakeCard();
        card.Padding = new Padding(14, 12, 14, 14);
        card.Margin = new Padding(0);

        var layout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = footer == null ? 2 : 3,
            BackColor = CardFace,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36 * UiScale)); // header
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));           // body fills remaining
        if (footer != null)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));           // footer

        var headerRow = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = headerTrailing == null ? 1 : 2,
            RowCount = 1,
            BackColor = CardFace,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 0, 4),
        };
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (headerTrailing != null)
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerRow.Controls.Add(SectionLabel(title), 0, 0);
        if (headerTrailing != null)
        {
            headerTrailing.Anchor = AnchorStyles.Right;
            headerTrailing.Margin = new Padding(12, 2, 0, 2);
            headerRow.Controls.Add(headerTrailing, 1, 0);
        }
        layout.Controls.Add(headerRow, 0, 0);

        bool isActivity = string.Equals(title, "Activity", StringComparison.OrdinalIgnoreCase);
        var bodyHost = new DoubleBufferedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = isActivity ? InputBg : CardFace,
            Padding = isActivity ? new Padding(8, 8, 8, 8) : new Padding(0),
        };
        if (isActivity)
        {
            UiTheme.BindRoundRegion(bodyHost, 12);
            bodyHost.Paint += (_, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = bodyHost.ClientRectangle;
                r.Width -= 1;
                r.Height -= 1;
                if (r.Width < 4 || r.Height < 4) return;
                using var path = CreateRoundRect(r, 12);
                using var fill = new SolidBrush(InputBg);
                using var border = new Pen(Color.FromArgb(150, CardBorder), 1);
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            };
        }
        body.Dock = DockStyle.Fill;
        bodyHost.Controls.Add(body);
        if (overlay != null)
        {
            overlay.Dock = DockStyle.Fill;
            bodyHost.Controls.Add(overlay);
            overlay.BringToFront();
        }
        layout.Controls.Add(bodyHost, 0, 1);
        if (footer != null)
            layout.Controls.Add(footer, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private static Panel MakeCard()
    {
        var card = new SoftCard
        {
            Dock = DockStyle.Fill,
            BackColor = CardFace,
            Margin = new Padding(0),
        };
        card.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = card.ClientRectangle;
            if (r.Width < 8 || r.Height < 8) return;
            r.Width -= 1;
            r.Height -= 1;
            if (r.Width < 8 || r.Height < 8) return;

            using var path = CreateRoundRect(r, 16);
            using (var brush = new LinearGradientBrush(r, Color.FromArgb(18, 27, 44), Color.FromArgb(13, 20, 34), 90f))
                g.FillPath(brush, path);

            using var border = new Pen(CardBorder, 1.0f);
            g.DrawPath(border, path);
        };
        return card;
    }

    private static GraphicsPath CreateRoundRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(1, radius * 2);
        var r = bounds;
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static SoftSectionTitle SectionLabel(string text) => new()
    {
        Title = text,
        Margin = new Padding(0),
        Padding = new Padding(2, 0, 0, 0),
    };

    private static Control PadBtn(Soft3dButton b)
    {
        b.Margin = new Padding(0, 0, 10, 6);
        return b;
    }

    private static void StyleTextBox(TextBox box)
    {
        box.BorderStyle = BorderStyle.None;
        box.BackColor = InputBg;
        box.ForeColor = TextPrimary;
        box.Height = 34;
    }

    private static void StylePrimaryButton(Soft3dButton b, string text) =>
        StyleSoftButton(b, text, Soft3dButton.Kind.Primary, minWidth: 118);

    private static void StyleSecondaryButton(Soft3dButton b, string text) =>
        StyleSoftButton(b, text, Soft3dButton.Kind.Secondary, minWidth: 96);

    private static void StyleDangerButton(Soft3dButton b, string text) =>
        StyleSoftButton(b, text, Soft3dButton.Kind.Danger, minWidth: 124);

    private static void StyleSoftButton(Soft3dButton b, string text, Soft3dButton.Kind kind, int minWidth)
    {
        var font = new Font("Segoe UI Semibold", 10.25f);
        var textWidth = TextRenderer.MeasureText(text, font).Width;
        b.Caption = text;
        b.ButtonKind = kind;
        b.AutoSize = false;
        b.Size = new Size(Math.Max(minWidth, textWidth + 44), ButtonHeight);
        b.Font = font;
        b.Cursor = Cursors.Hand;
    }

    /// <summary>Apple-like soft 3D button: rounded, gradient fill, specular highlight, soft shadow.</summary>
    private sealed class Soft3dButton : Control, IButtonControl
    {
        public enum Kind { Primary, Secondary, Danger }

        private bool _hover;
        private bool _pressed;
        private string _caption = "";

        public Kind ButtonKind { get; set; } = Kind.Primary;
        public DialogResult DialogResult { get; set; } = DialogResult.None;

        /// <summary>Visible label — kept separate so the framework never paints a second copy.</summary>
        public string Caption
        {
            get => _caption;
            set
            {
                _caption = value ?? "";
                Invalidate();
            }
        }

        public override string Text
        {
            get => _caption;
#pragma warning disable CS8765
            set => Caption = value ?? "";
#pragma warning restore CS8765
        }

        public Soft3dButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.Selectable |
                     ControlStyles.StandardClick |
                     ControlStyles.StandardDoubleClick, true);
            DoubleBuffered = true;
            BackColor = CardFace;
            ForeColor = Color.White;
            TabStop = true;
        }

        public void NotifyDefault(bool value) => Invalidate();

        public void PerformClick()
        {
            if (CanSelect)
                OnClick(EventArgs.Empty);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (DialogResult != DialogResult.None)
            {
                var form = FindForm();
                if (form != null)
                    form.DialogResult = DialogResult;
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Focus();
                _pressed = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Space or Keys.Enter)
            {
                _pressed = true;
                Invalidate();
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Space or Keys.Enter)
            {
                _pressed = false;
                Invalidate();
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var radius = 10;
            var bodyRect = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));

            if (bodyRect.Width < 8 || bodyRect.Height < 8)
                return;

            GetPalette(out var top, out var bottom, out var rim, out var text);

            if (!Enabled)
            {
                top = Color.FromArgb(14, 20, 30);
                bottom = Color.FromArgb(11, 16, 24);
                rim = Color.FromArgb(30, 42, 58);
                text = Color.FromArgb(80, 100, 130);
            }
            else if (_pressed)
            {
                top = Darken(top, 0.15f);
                bottom = Darken(bottom, 0.15f);
            }
            else if (_hover)
            {
                top = Lighten(top, 0.12f);
                bottom = Lighten(bottom, 0.08f);
                rim = ButtonKind == Kind.Secondary ? Accent : Lighten(rim, 0.15f);
            }

            using (var bodyPath = CreateRoundRect(bodyRect, radius))
            {
                if (Enabled && (_hover || ButtonKind == Kind.Primary))
                {
                    var glowRect = Rectangle.Inflate(bodyRect, 1, 1);
                    using var glowPath = CreateRoundRect(glowRect, radius + 1);
                    using var glowPen = new Pen(Color.FromArgb(_hover ? 55 : 30, rim), 2f);
                    g.DrawPath(glowPen, glowPath);
                }

                using (var fill = new LinearGradientBrush(bodyRect, top, bottom, 90f))
                    g.FillPath(fill, bodyPath);

                var rimAlpha = !Enabled ? 40 : (_hover ? 255 : (ButtonKind == Kind.Secondary ? 90 : 220));
                using var rimPen = new Pen(Color.FromArgb(rimAlpha, rim), ButtonKind == Kind.Primary ? 1.2f : 1.4f);
                g.DrawPath(rimPen, bodyPath);

                if (Focused && ShowFocusCues)
                {
                    var focus = Rectangle.Inflate(bodyRect, -2, -2);
                    if (focus.Width > 4 && focus.Height > 4)
                    {
                        using var focusPath = CreateRoundRect(focus, Math.Max(1, radius - 2));
                        using var focusPen = new Pen(Color.FromArgb(140, Accent)) { DashStyle = DashStyle.Dot };
                        g.DrawPath(focusPen, focusPath);
                    }
                }
            }

            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
                        TextFormatFlags.GlyphOverhangPadding;
            var textRect = bodyRect;
            if (_pressed) textRect.Offset(0, 1);

            TextRenderer.DrawText(g, _caption, Font, textRect, text, flags);
        }

        private void GetPalette(out Color top, out Color bottom, out Color rim, out Color text)
        {
            switch (ButtonKind)
            {
                case Kind.Secondary:
                    top = Color.FromArgb(19, 29, 44);
                    bottom = Color.FromArgb(14, 22, 34);
                    rim = Color.FromArgb(42, 60, 88);
                    text = Color.FromArgb(235, 242, 250);
                    break;
                case Kind.Danger:
                    top = Color.FromArgb(34, 16, 24);
                    bottom = Color.FromArgb(24, 11, 17);
                    rim = Color.FromArgb(225, 29, 72);
                    text = Color.FromArgb(255, 160, 175);
                    break;
                default:
                    top = Color.FromArgb(0, 210, 240);
                    bottom = Color.FromArgb(0, 175, 215);
                    rim = Color.FromArgb(0, 240, 255);
                    text = Color.FromArgb(5, 17, 29);
                    break;
            }
        }

        private static Color Lighten(Color c, float amount)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            return Color.FromArgb(c.A,
                (int)(c.R + (255 - c.R) * amount),
                (int)(c.G + (255 - c.G) * amount),
                (int)(c.B + (255 - c.B) * amount));
        }

        private static Color Darken(Color c, float amount)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            return Color.FromArgb(c.A,
                (int)(c.R * (1 - amount)),
                (int)(c.G * (1 - amount)),
                (int)(c.B * (1 - amount)));
        }
    }

    private class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    /// <summary>Card whose corners show the window backdrop so rounding sits on the circuit.</summary>
    private sealed class SoftCard : DoubleBufferedPanel
    {
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (FindForm() is MainForm form)
                form.PaintBackdrop(e.Graphics, this);
            else
                base.OnPaintBackground(e);
        }
    }

    internal void PaintBackdrop(Graphics g, Control child)
    {
        EnsureWindowBackgroundCache();
        if (_windowBgCache == null || child.Width <= 0 || child.Height <= 0)
        {
            using var fill = new SolidBrush(BgDeep);
            g.FillRectangle(fill, child.ClientRectangle);
            return;
        }
        var loc = PointToClient(child.PointToScreen(Point.Empty));
        g.DrawImage(_windowBgCache,
            new Rectangle(0, 0, child.Width, child.Height),
            new Rectangle(loc.X, loc.Y, child.Width, child.Height),
            GraphicsUnit.Pixel);
    }

    private sealed class BufferedTableLayoutPanel : TableLayoutPanel
    {
        public BufferedTableLayoutPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            DoubleBuffered = true;
        }
    }

    private sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
    {
        public BufferedFlowLayoutPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            DoubleBuffered = true;
        }
    }

    private void InvalidateWindowBackgroundCache()
    {
        _windowBgCache?.Dispose();
        _windowBgCache = null;
        _windowBgCacheSize = Size.Empty;
    }

    private void EnsureWindowBackgroundCache()
    {
        var size = ClientSize;
        if (size.Width <= 0 || size.Height <= 0)
            return;
        if (_windowBgCache != null && _windowBgCacheSize == size)
            return;

        InvalidateWindowBackgroundCache();
        var bmp = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
            PaintWindowBackground(g, new Rectangle(Point.Empty, size));
        _windowBgCache = bmp;
        _windowBgCacheSize = size;
    }

    private void PaintWindowBackground(Graphics g, Rectangle bounds, bool fast = false)
    {
        using (var brush = new LinearGradientBrush(bounds, BgDeep, BgMid, 90f))
            g.FillRectangle(brush, bounds);

        var img = _circuitBackground;
        if (img != null && bounds.Width > 0 && bounds.Height > 0)
        {
            var scale = Math.Max(bounds.Width / (float)img.Width, bounds.Height / (float)img.Height);
            var w = (int)Math.Ceiling(img.Width * scale);
            var h = (int)Math.Ceiling(img.Height * scale);
            var x = bounds.X + (bounds.Width - w) / 2;
            var y = bounds.Y + (bounds.Height - h) / 2;
            var oldInterp = g.InterpolationMode;
            g.InterpolationMode = fast
                ? InterpolationMode.Low
                : InterpolationMode.HighQualityBilinear;

            using (var ia = new System.Drawing.Imaging.ImageAttributes())
            {
                var matrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.32f };
                ia.SetColorMatrix(matrix, System.Drawing.Imaging.ColorMatrixFlag.Default, System.Drawing.Imaging.ColorAdjustType.Bitmap);
                g.DrawImage(img, new Rectangle(x, y, w, h), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
            }
            g.InterpolationMode = oldInterp;
        }

        if (bounds.Width > 0 && bounds.Height > 0)
        {
            using var glow = new LinearGradientBrush(
                new Rectangle(bounds.Width / 4, 0, Math.Max(1, bounds.Width / 2), 160),
                Color.FromArgb(22, Accent),
                Color.FromArgb(0, Accent),
                90f);
            g.FillRectangle(glow, bounds.Width / 4, 0, Math.Max(1, bounds.Width / 2), 160);
        }
    }

    private static Image? LoadCircuitBackground()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "bg-circuit.png");
            if (File.Exists(path))
                return Image.FromFile(path);
        }
        catch { /* ignore */ }
        return null;
    }

    internal void PopulateMockupState()
    {
        _mockupMode = true;
        _searchPulseTimer?.Stop();
        _pingTimer?.Stop();
        _portPollTimer?.Stop();
        _inventoryTimer?.Stop();
        StopPhoneWatch();

        _connectHero.Headline = "Connected";
        _connectHero.HeadlineColor = Accent;
        _connectHero.Hint = "";

        _serverList.Items.Clear();
        var s1 = new OnlineServer("192.168.68.61", "Wi-Fi host...")
        {
            BatteryPercent = 12,
            LastRttMs = 55,
        };
        var s2 = new OnlineServer("192.168.68.66", "USB hosts");
        _serverList.Items.Add(s1);
        _serverList.Items.Add(s2);
        _serverList.SelectedIndex = 0;
        UpdateListEmptyVisible(_serverList, _serversEmpty);

        _deviceList.Items.Clear();
        _devicesEmpty.SetCopy("All devices in use", "");
        UpdateDevicesEmptyVisible();

        _attachedList.Items.Clear();
        var a1 = new AttachedUsbDevice(
            Port: 1,
            Description: "CX 2.4G Receiver",
            Vid: null,
            Pid: null,
            RemoteHost: "192.168.68.61",
            BusId: null,
            Speed: null,
            PhoneLabel: "CX 2.4G Receiver",
            AutoConnect: true);
        _attachedCache = new List<AttachedUsbDevice> { a1 };
        _attachedList.Items.Add(a1);
        _attachedList.SelectedIndex = -1;
        UpdateListEmptyVisible(_attachedList, _attachedEmpty);


        _logBox.Clear();
        _logBox.Append("2021-09-07 12:20:02", "Information USB Contenal USB devices");
        _logBox.Append("2021-09-07 12:20:02", "Internecting USB device...");
        _logBox.Append("2021-09-07 22:27:03", "Informtion strts 'CPU Roentist command'.");
        _logBox.Append("2021-09-07 12:27:02", "Informting host device...");
    }

    private void ToggleManualAddress()
    {
        _manualAddressVisible = !_manualAddressVisible;
        if (_manualRow != null)
            _manualRow.Visible = _manualAddressVisible;
        if (_savedRow != null)
            _savedRow.Visible = _manualAddressVisible;
        _manualToggle.Text = _manualAddressVisible
            ? "Hide manual address"
            : "Enter address manually";
        _manualToggle.LinkArea = new LinkArea(0, _manualToggle.Text.Length);
        
        // Force WinForms to recalculate nested AutoSize bounds before focusing
        _manualRow?.Parent?.PerformLayout();

        if (_manualAddressVisible)
            _hostBox.Focus();
    }

    private void SetStatus(string text, Color color)
    {
        // Status line removed from the header — keep Activity log for details.
        _ = text;
        _ = color;
    }

    private void UpdateListEmptyVisible(SoftListBox list, SoftEmptyState empty)
    {
        var showEmpty = list.Items.Count == 0;
        empty.Visible = showEmpty;
        list.Visible = !showEmpty;
        if (showEmpty) empty.BringToFront();
        else list.BringToFront();
    }

    private void UpdateDevicesEmptyVisible() => UpdateListEmptyVisible(_deviceList, _devicesEmpty);

    /// <summary>Show Disconnect controls only when Active has at least one device.</summary>
    private void UpdateDisconnectUi()
    {
        var hasActive = _attachedList.Items.Count > 0;
        {
            var target = hasActive ? 96f * UiScale : 0f;
        }
    }

    private static bool IsAttachedMatch(RemoteUsbDevice remote, AttachedUsbDevice attached, string? listHost)
    {
        // Prefer bus id — that's the USB/IP identity.
        if (!string.IsNullOrEmpty(attached.BusId) &&
            string.Equals(remote.BusId, attached.BusId, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(attached.RemoteHost) || string.IsNullOrWhiteSpace(listHost))
                return true;
            return string.Equals(attached.RemoteHost, listHost, StringComparison.OrdinalIgnoreCase);
        }

        // Fallback: same VID:PID from the same host.
        if (remote.Vid != null && remote.Pid != null &&
            attached.Vid != null && attached.Pid != null &&
            string.Equals(remote.Vid, attached.Vid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(remote.Pid, attached.Pid, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(attached.RemoteHost) || string.IsNullOrWhiteSpace(listHost))
                return true;
            return string.Equals(attached.RemoteHost, listHost, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private void PopulateAvailableDevices(IEnumerable<RemoteUsbDevice> devices, string host)
    {
        _remoteCache = devices
            .Where(d => !IsTombstoned(d.BusId, d.Vid, d.Pid))
            .Select(WithPhoneLabel)
            .ToList();
        var available = _remoteCache
            .Where(d => !_attachedCache.Any(a => IsAttachedMatch(d, a, host)))
            .Select(d => d with { AutoConnect = IsAutoConnectTarget(d) })
            .ToList();
        _remoteCache = _remoteCache
            .Select(d => d with { AutoConnect = IsAutoConnectTarget(d) })
            .ToList();

        var signature = string.Join("|", available.Select(d =>
            $"{d.BusId}\u001f{d.DisplayName}\u001f{d.Vid}:{d.Pid}\u001f{(d.AutoConnect ? "1" : "0")}\u001f{d.ClassHint}"));
        var current = string.Join("|", _deviceList.Items.Cast<object>()
            .Select(o => o is RemoteUsbDevice d
                ? $"{d.BusId}\u001f{d.DisplayName}\u001f{d.Vid}:{d.Pid}\u001f{(d.AutoConnect ? "1" : "0")}\u001f{d.ClassHint}"
                : o?.ToString() ?? ""));
        if (string.Equals(signature, current, StringComparison.Ordinal))
        {
            UpdateDevicesEmptyVisible();
            MaybeAutoConnectFromAvailable(available);
            PruneAutoConnectHoldoff(available);
            return;
        }

        var selectedBus = (_deviceList.SelectedItem as RemoteUsbDevice)?.BusId;
        _deviceList.BeginUpdate();
        _deviceList.Items.Clear();
        foreach (var d in available)
            _deviceList.Items.Add(d);
        _deviceList.EndUpdate();
        UpdateDevicesEmptyVisible();

        if (available.Count == 0)
        {
            string title;
            string body;
            if (_attachedCache.Count > 0)
            {
                title = "All devices in use";
                body = "";
            }
            else if (BusyElsewhereCopy(out var detail))
            {
                title = "In use on another PC";
                body = detail ?? "Detach there first.";
            }
            else
            {
                title = "No devices yet";
                body = "";
            }
            _devicesEmpty.SetCopy(title, body);
        }
        else
        {
            _devicesEmpty.SetCopy("No devices yet", "");
            if (selectedBus != null)
            {
                for (var i = 0; i < _deviceList.Items.Count; i++)
                {
                    if (_deviceList.Items[i] is RemoteUsbDevice r &&
                        string.Equals(r.BusId, selectedBus, StringComparison.OrdinalIgnoreCase))
                    {
                        _deviceList.SelectedIndex = i;
                        break;
                    }
                }
            }
            if (_deviceList.SelectedIndex < 0)
                _deviceList.SelectedIndex = 0;
        }

        MaybeAutoConnectFromAvailable(available);
        PruneAutoConnectHoldoff(available);
    }

    private void RememberServer(string host, string name = "UsbNetBridge")
    {
        if (string.IsNullOrWhiteSpace(host)) return;
        host = host.Trim();
        if (IsRecentlyStopped(host)) return;
        var changed = false;
        if (_onlineServers.TryGetValue(host, out var existing))
        {
            existing.LastSeenUtc = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(name) &&
                !string.Equals(existing.Name, name, StringComparison.Ordinal))
            {
                existing.Name = name;
                changed = true;
            }
        }
        else
        {
            _onlineServers[host] = new OnlineServer(host, string.IsNullOrWhiteSpace(name) ? "UsbNetBridge" : name);
            changed = true;
        }
        if (changed)
        {
            RefreshServerListUi();
            _ = PingOnlineServersAsync();
        }
        else
            UpdateDiscoverStatusText();
    }

    private void RememberServer(LanDiscovery.FoundServer server)
    {
        var advertised = server.Host?.Trim();
        var source = server.FromAddress?.Trim();
        var host = !string.IsNullOrWhiteSpace(source) && source != "0.0.0.0"
            ? source
            : advertised;
        if (string.IsNullOrWhiteSpace(host)) return;

        var changed = false;
        // A live UNB1! reply means the phone started again — don't keep ignoring it.
        _stoppedHostsUntil.Remove(host);
        if (!string.IsNullOrWhiteSpace(source))
            _stoppedHostsUntil.Remove(source);
        if (!string.IsNullOrWhiteSpace(advertised))
        {
            _stoppedHostsUntil.Remove(advertised);
            // Drop the unreachable LAN-IP row if we now have the VPN/source IP.
            if (!string.Equals(advertised, host, StringComparison.OrdinalIgnoreCase) &&
                _onlineServers.Remove(advertised))
                changed = true;
        }
        if (!_onlineServers.TryGetValue(host, out var existing))
        {
            existing = new OnlineServer(host, string.IsNullOrWhiteSpace(server.Name) ? "UsbNetBridge" : server.Name);
            _onlineServers[host] = existing;
            changed = true;
        }
        existing.LastSeenUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(server.Name) &&
            !string.Equals(existing.Name, server.Name, StringComparison.Ordinal))
        {
            existing.Name = server.Name;
            changed = true;
        }
        if (server.ReportsPluggedDevices)
        {
            existing.ReportsPluggedDevices = true;
            existing.PluggedBusIds.Clear();
            existing.PluggedVidPids.Clear();
            existing.AdvertisedDevices.Clear();
            foreach (var d in server.PluggedDevices)
            {
                if (IsTombstoned(d.BusId, d.Vid, d.Pid))
                    continue;
                if (!string.IsNullOrWhiteSpace(d.BusId))
                    existing.PluggedBusIds.Add(d.BusId.Trim());
                if (!string.IsNullOrWhiteSpace(d.Vid) && !string.IsNullOrWhiteSpace(d.Pid))
                    existing.PluggedVidPids.Add($"{d.Vid}:{d.Pid}");
                RememberPhoneLabel(d.BusId, d.Vid, d.Pid, d.Label);
                RememberPhoneClass(d.BusId, d.Vid, d.Pid, d.ClassHint);
                existing.AdvertisedDevices.Add(new RemoteUsbDevice(
                    d.BusId,
                    d.Label ?? "USB device",
                    d.Vid,
                    d.Pid,
                    d.Label,
                    ClassHint: d.ClassHint));
            }
        }
        var healthBefore = existing.StatusLine + existing.StatusChip + existing.BatteryChip + existing.BusyHost + existing.BusyPc;
        existing.BatteryPercent = server.BatteryPercent;
        existing.BatteryCharging = server.BatteryCharging;
        existing.WifiQuality = server.WifiQuality;
        existing.BusyHost = server.BusyHost;
        existing.BusyPc = server.BusyPc;
        if (!string.Equals(healthBefore,
                existing.StatusLine + existing.StatusChip + existing.BatteryChip + existing.BusyHost + existing.BusyPc,
                StringComparison.Ordinal))
            _serverList.Invalidate();
        if (changed)
        {
            RefreshServerListUi();
            _ = PingOnlineServersAsync();
        }
        else
            UpdateDiscoverStatusText();

        if (existing.AdvertisedDevices.Count > 0)
            ShowAdvertisedDevicesIfNeeded(host);
    }

    private void ShowAdvertisedDevicesIfNeeded(string host)
    {
        if (_deviceList.Items.Count > 0 || _attachedCache.Count > 0)
            return;
        var advertised = _onlineServers.Values
            .SelectMany(s => s.AdvertisedDevices)
            .GroupBy(d => d.BusId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (advertised.Count == 0)
            return;
        Log($"USB host advertised {advertised.Count} USB device(s).");
        PopulateAvailableDevices(advertised, host);
    }

    private void RememberPhoneLabel(string? busId, string? vid, string? pid, string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return;
        label = label.Trim();
        if (!string.IsNullOrWhiteSpace(busId))
            _phoneLabelsByBusId[busId.Trim()] = label;
        if (!string.IsNullOrWhiteSpace(vid) && !string.IsNullOrWhiteSpace(pid))
            _phoneLabelsByVidPid[$"{vid}:{pid}"] = label;
    }

    private void RememberPhoneClass(string? busId, string? vid, string? pid, string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return;
        hint = hint.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(busId))
            _phoneClassByBusId[busId.Trim()] = hint;
        if (!string.IsNullOrWhiteSpace(vid) && !string.IsNullOrWhiteSpace(pid))
            _phoneClassByVidPid[$"{vid}:{pid}"] = hint;
    }

    private string? LookupPhoneClass(string? busId, string? vid, string? pid)
    {
        if (!string.IsNullOrWhiteSpace(busId) &&
            _phoneClassByBusId.TryGetValue(busId.Trim(), out var byBus))
            return byBus;
        if (!string.IsNullOrWhiteSpace(vid) && !string.IsNullOrWhiteSpace(pid) &&
            _phoneClassByVidPid.TryGetValue($"{vid}:{pid}", out var byId))
            return byId;
        return null;
    }

    private string? LookupPhoneLabel(string? busId, string? vid, string? pid)
    {
        if (!string.IsNullOrWhiteSpace(busId) &&
            _phoneLabelsByBusId.TryGetValue(busId.Trim(), out var byBus) &&
            !string.IsNullOrWhiteSpace(byBus))
            return byBus;
        if (!string.IsNullOrWhiteSpace(vid) && !string.IsNullOrWhiteSpace(pid) &&
            _phoneLabelsByVidPid.TryGetValue($"{vid}:{pid}", out var byId) &&
            !string.IsNullOrWhiteSpace(byId))
            return byId;
        return null;
    }

    private RemoteUsbDevice WithPhoneLabel(RemoteUsbDevice d) =>
        d with
        {
            PhoneLabel = LookupPhoneLabel(d.BusId, d.Vid, d.Pid) ?? d.PhoneLabel,
            ClassHint = LookupPhoneClass(d.BusId, d.Vid, d.Pid) ?? d.ClassHint,
        };

    private AttachedUsbDevice WithPhoneLabel(AttachedUsbDevice d)
    {
        var label = LookupPhoneLabel(d.BusId, d.Vid, d.Pid) ?? d.PhoneLabel;
        // Fall back to the Available-list entry we already labeled from the phone.
        if (string.IsNullOrWhiteSpace(label))
        {
            var host = _hostBox.Text.Trim();
            var remote = _remoteCache.FirstOrDefault(r => IsAttachedMatch(r, d, host));
            if (!string.IsNullOrWhiteSpace(remote?.DisplayName) &&
                !remote!.DisplayName.Equals("USB device", StringComparison.OrdinalIgnoreCase))
                label = remote.DisplayName;
        }
        if (!string.IsNullOrWhiteSpace(label))
            RememberPhoneLabel(d.BusId, d.Vid, d.Pid, label);
        return d with { PhoneLabel = label };
    }

    private void StartPhoneEventListener()
    {
        _phoneEvents = new PhoneEventListener();
        _phoneEvents.Log += msg =>
        {
            if (!IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(() => Log(msg));
            else Log(msg);
        };
        _phoneEvents.DeviceGone += evt =>
        {
            if (!IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(() => _ = HandlePhoneDeviceGoneAsync(evt));
            else _ = HandlePhoneDeviceGoneAsync(evt);
        };
        _phoneEvents.ServerOffline += evt =>
        {
            if (!IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(() => _ = HandlePhoneServerOfflineAsync(evt));
            else _ = HandlePhoneServerOfflineAsync(evt);
        };
        _phoneEvents.Start();
    }

    /// <summary>Phone pushed an explicit unplug event — clear matching Active ports now.</summary>
    private async Task HandlePhoneDeviceGoneAsync(PhoneEventListener.DeviceGoneEvent evt)
    {
        if (_cli == null) return;
        // Local Disconnect / Disconnect all also closes the phone TCP session; ignore echo "gone" events.
        if (DateTime.UtcNow < _suppressGoneUntilUtc)
        {
            Log("Ignoring USB host unplug event (local disconnect in progress).");
            return;
        }
        if (_detachStaleBusy) return;
        _detachStaleBusy = true;
        var lastDeviceOnPhone = false;
        try
        {
            // Do not RememberServer here — that kept the Servers row alive for ~5s after
            // the last USB unplug (Android auto-stops with nothing left to share).
            foreach (var server in _onlineServers.Values)
            {
                server.PluggedBusIds.Remove(evt.BusId);
                if (evt.Vid != null && evt.Pid != null)
                    server.PluggedVidPids.Remove($"{evt.Vid}:{evt.Pid}");
                server.AdvertisedDevices.RemoveAll(r => IsGoneRemote(r, evt));
            }
            TombstoneGone(evt.BusId, evt.Vid, evt.Pid);

            // Capture before port refresh — TCP may already have dropped the VHCI port.
            var prior = _attachedCache.ToList();
            var matches = prior.Where(d => IsSameRemoteDevice(d, evt.BusId, evt.Vid, evt.Pid)).ToList();

            await ApplyPortStatusAsync(updateStatusWhenAttached: false);

            // If port list cleared before the event arrived and we only had one Active device,
            // treat that as the unplugged one.
            if (matches.Count == 0 && prior.Count == 1)
                matches = prior.ToList();

            foreach (var d in matches)
            {
                try
                {
                    // Port may already be free — detach is best-effort.
                    Log($"Clearing Active port {d.Port:D2} ({d.DisplayName}) — USB host unplugged device.");
                    await _cli.DetachAsync(d.Port);
                }
                catch (Exception ex)
                {
                    Log($"Could not clear port {d.Port:D2}: {ex.Message}");
                }
            }

            await ApplyPortStatusAsync(updateStatusWhenAttached: false);

            var host = _hostBox.Text.Trim();
            _remoteCache = _remoteCache.Where(r => !IsGoneRemote(r, evt)).ToList();
            if (_remoteCache.Count > 0)
                PopulateAvailableDevices(_remoteCache, host);
            else
                ClearAvailableDevices();

            var noCachedDevices = _attachedCache.Count == 0 && _remoteCache.Count == 0;
            var advertisedEmpty = _onlineServers.Values.Any(s => s.ReportsPluggedDevices) &&
                                  _onlineServers.Values.Where(s => s.ReportsPluggedDevices)
                                      .All(s => s.PluggedBusIds.Count == 0);
            lastDeviceOnPhone = noCachedDevices || advertisedEmpty;

            if (_attachedCache.Count == 0 && !lastDeviceOnPhone)
            {
                SetStatus("Ready — find a USB host, then pick a USB device", OkGreen);
                _connectHero.Hint = "Device was unplugged from the USB host.";
            }

            if (matches.Count > 0)
                ShowDeviceDisconnectedPopup(matches, DeviceGoneReason.UnpluggedOnPhone);
        }
        finally
        {
            _detachStaleBusy = false;
        }

        if (lastDeviceOnPhone)
        {
            await HandlePhoneServerOfflineAsync(
                new PhoneEventListener.ServerOfflineEvent(new[] { evt.PhoneHost }));
            return;
        }

        if (!string.IsNullOrWhiteSpace(_hostBox.Text))
            await RefreshDevicesAsync(quiet: true);
    }

    /// <summary>Phone pushed an explicit Stop — drop the Servers row and Available list now.</summary>
    private async Task HandlePhoneServerOfflineAsync(PhoneEventListener.ServerOfflineEvent evt)
    {
        lock (_offlineGate)
        {
            if (DateTime.UtcNow < _offlineHandledUntilUtc)
                return;
            // Phone retries UNB1-off on several ports/IPs (often 6 packets). One handling is enough.
            _offlineHandledUntilUtc = DateTime.UtcNow.AddSeconds(8);
        }

        var hosts = _onlineServers.Keys.ToList();
        foreach (var host in evt.Hosts)
            MarkHostStopped(host);
        foreach (var host in hosts)
        {
            MarkHostStopped(host);
            _onlineServers.Remove(host);
        }
        // Packet source / advertised IPs may differ from the row we showed (Wi‑Fi vs VPN).
        foreach (var host in ResolveOfflineHosts(evt.Hosts))
        {
            MarkHostStopped(host);
            _onlineServers.Remove(host);
        }

        var wasActive = _attachedCache.ToList();
        if (wasActive.Count == 0 && _cli != null)
        {
            await ApplyPortStatusAsync(updateStatusWhenAttached: false);
            wasActive = _attachedCache.ToList();
        }

        ClearAvailableDevices();
        RefreshServerListUi();
        Log("USB host stopped sharing.");

        SetStatus("Waiting for a USB host on the network…", Accent);
        _connectHero.Hint = "";
        StartPhoneWatch();

        await DetachPortsForHostsAsync(hosts.Count > 0 ? hosts : evt.Hosts);
        if (wasActive.Count > 0)
            ShowDeviceDisconnectedPopup(wasActive, DeviceGoneReason.PhoneStopped);
    }

    private void MarkHostStopped(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || host == "0.0.0.0") return;
        _stoppedHostsUntil[host.Trim()] = DateTime.UtcNow + StoppedHostGrace;
    }

    private bool IsRecentlyStopped(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        if (!_stoppedHostsUntil.TryGetValue(host, out var until))
            return false;
        if (DateTime.UtcNow < until)
            return true;
        _stoppedHostsUntil.Remove(host);
        return false;
    }

    private void ForgetUnseenServers(IReadOnlyList<LanDiscovery.FoundServer> found)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in found)
        {
            if (!string.IsNullOrWhiteSpace(s.Host))
                seen.Add(s.Host.Trim());
            if (!string.IsNullOrWhiteSpace(s.FromAddress))
                seen.Add(s.FromAddress.Trim());
        }

        var missing = _onlineServers.Keys.Where(k => !seen.Contains(k)).ToList();
        if (missing.Count == 0) return;

        foreach (var h in missing)
            _onlineServers.Remove(h);
        if (_onlineServers.Count == 0)
            ClearAvailableDevices();
        RefreshServerListUi();
        Log(missing.Count == 1
            ? $"Server {missing[0]} is no longer advertising."
            : $"{missing.Count} servers are no longer advertising.");
    }

    private List<string> ResolveOfflineHosts(IReadOnlyList<string> advertised)
    {
        var candidates = new HashSet<string>(advertised ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var typed = _hostBox.Text.Trim();
        if (!string.IsNullOrEmpty(typed))
            candidates.Add(typed);

        var matched = _onlineServers.Keys
            .Where(k => candidates.Contains(k))
            .ToList();

        // VPN peer IP vs advertised LAN IP — one phone online is the one that stopped.
        if (matched.Count == 0 && _onlineServers.Count == 1)
            matched.Add(_onlineServers.Keys.First());

        if (matched.Count == 0)
            matched.AddRange(candidates.Where(h => !string.IsNullOrWhiteSpace(h)));

        return matched.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void ClearAvailableDevices()
    {
        _remoteCache = Array.Empty<RemoteUsbDevice>();
        if (_deviceList.Items.Count > 0)
        {
            _deviceList.BeginUpdate();
            _deviceList.Items.Clear();
            _deviceList.EndUpdate();
        }
        _devicesEmpty.SetCopy("No devices yet", "");
        UpdateDevicesEmptyVisible();
    }

    private async Task DetachPortsForHostsAsync(IReadOnlyList<string> hosts)
    {
        if (_cli == null || hosts.Count == 0) return;
        for (var i = 0; i < 20 && _detachStaleBusy; i++)
            await Task.Delay(50);
        if (_detachStaleBusy) return;
        _detachStaleBusy = true;
        try
        {
            await ApplyPortStatusAsync(updateStatusWhenAttached: false);
            var matches = _attachedCache.Where(d =>
            {
                if (hosts.Any(h => string.Equals(h, d.RemoteHost, StringComparison.OrdinalIgnoreCase)))
                    return true;
                return string.IsNullOrWhiteSpace(d.RemoteHost) && _onlineServers.Count == 0;
            }).ToList();
            foreach (var d in matches)
            {
                try
                {
                    Log($"Clearing Active port {d.Port:D2} ({d.DisplayName}) — USB host stopped sharing.");
                    await _cli.DetachAsync(d.Port);
                }
                catch (Exception ex)
                {
                    Log($"Could not clear port {d.Port:D2}: {ex.Message}");
                }
            }
            await ApplyPortStatusAsync(updateStatusWhenAttached: false);
        }
        finally
        {
            _detachStaleBusy = false;
        }
    }

    private void TombstoneGone(string? busId, string? vid, string? pid)
    {
        var until = DateTime.UtcNow.AddMilliseconds(3500);
        if (!string.IsNullOrWhiteSpace(busId))
            _goneUntil[busId.Trim()] = until;
        if (!string.IsNullOrWhiteSpace(vid) && !string.IsNullOrWhiteSpace(pid))
            _goneUntil[$"{vid}:{pid}"] = until;
    }

    private bool IsTombstoned(string? busId, string? vid, string? pid)
    {
        var now = DateTime.UtcNow;
        if (_goneUntil.Count > 0)
        {
            foreach (var key in _goneUntil.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
                _goneUntil.Remove(key);
        }
        if (!string.IsNullOrWhiteSpace(busId) &&
            _goneUntil.TryGetValue(busId.Trim(), out var byBus) && now < byBus)
            return true;
        if (!string.IsNullOrWhiteSpace(vid) && !string.IsNullOrWhiteSpace(pid) &&
            _goneUntil.TryGetValue($"{vid}:{pid}", out var byId) && now < byId)
            return true;
        return false;
    }

    private static bool IsGoneRemote(RemoteUsbDevice remote, PhoneEventListener.DeviceGoneEvent evt)
    {
        if (!string.IsNullOrWhiteSpace(evt.BusId) &&
            !string.IsNullOrWhiteSpace(remote.BusId) &&
            string.Equals(remote.BusId, evt.BusId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(evt.Vid) && !string.IsNullOrWhiteSpace(evt.Pid) &&
            !string.IsNullOrWhiteSpace(remote.Vid) && !string.IsNullOrWhiteSpace(remote.Pid) &&
            string.Equals(remote.Vid, evt.Vid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(remote.Pid, evt.Pid, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static bool IsSameRemoteDevice(AttachedUsbDevice attached, string busId, string? vid, string? pid)
    {
        if (!string.IsNullOrWhiteSpace(busId) &&
            !string.IsNullOrWhiteSpace(attached.BusId) &&
            string.Equals(attached.BusId, busId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(vid) && !string.IsNullOrWhiteSpace(pid) &&
            !string.IsNullOrWhiteSpace(attached.Vid) && !string.IsNullOrWhiteSpace(attached.Pid) &&
            string.Equals(attached.Vid, vid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(attached.Pid, pid, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Drop local usbip ports whose remote busid/VID:PID is no longer plugged on the phone.
    /// Requires a discovery reply that includes <c>dev=</c> (new Android builds).
    /// </summary>
    private async Task DetachStaleAttachesAsync()
    {
        if (_cli == null || _detachStaleBusy) return;
        _detachStaleBusy = true;
        try
        {
            IReadOnlyList<AttachedUsbDevice> attached = _attachedCache;
            if (attached.Count == 0) return;

            // Prefer servers that report a live device list (any host — attach IP may differ from beacon IP).
            var reporters = _onlineServers.Values.Where(s => s.ReportsPluggedDevices).ToList();
            if (reporters.Count == 0) return;

            var stale = new List<AttachedUsbDevice>();
            foreach (var d in attached)
            {
                OnlineServer? server = null;
                if (!string.IsNullOrWhiteSpace(d.RemoteHost))
                    server = reporters.FirstOrDefault(s =>
                        string.Equals(s.Host, d.RemoteHost, StringComparison.OrdinalIgnoreCase));
                // VPN peer IP vs discovery LAN IP — if only one phone is online, use it.
                if (server == null && reporters.Count == 1)
                    server = reporters[0];
                if (server == null)
                    continue;
                if (!ServerStillHasDevice(server, d))
                    stale.Add(d);
            }

            if (stale.Count == 0) return;

            foreach (var d in stale)
            {
                try
                {
                    Log($"USB host unplugged {d.DisplayName} [{d.BusId}] — clearing local port {d.Port:D2}.");
                    await _cli.DetachAsync(d.Port);
                }
                catch (Exception ex)
                {
                    Log($"Could not clear stale port {d.Port:D2}: {ex.Message}");
                }
            }

            await ApplyPortStatusAsync(updateStatusWhenAttached: false);
            if (_attachedCache.Count == 0)
            {
                SetStatus("Ready — find a USB host, then pick a USB device", OkGreen);
                _connectHero.Hint = "Device was unplugged from the USB host.";
            }
            ShowDeviceDisconnectedPopup(stale, DeviceGoneReason.UnpluggedOnPhone);
        }
        finally
        {
            _detachStaleBusy = false;
        }
    }

    private enum DeviceGoneReason
    {
        UnpluggedOnPhone,
        LocalDisconnect,
        PhoneStopped,
    }

    private void ShowDeviceDisconnectedPopup(IReadOnlyList<AttachedUsbDevice> devices, DeviceGoneReason reason)
    {
        if (devices.Count == 0) return;

        var names = devices
            .Select(d =>
            {
                var id = d.Vid != null && d.Pid != null ? $" ({d.Vid}:{d.Pid})" : "";
                return $"• {d.DisplayName}{id}";
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var list = string.Join(Environment.NewLine, names);
        var (title, reasonText) = reason switch
        {
            DeviceGoneReason.UnpluggedOnPhone =>
                ("Device disconnected", "It was unplugged from the USB host."),
            DeviceGoneReason.PhoneStopped =>
                ("Sharing stopped", "UsbNetBridge was stopped on the USB host."),
            _ =>
                ("Device disconnected", "You disconnected it on this PC."),
        };
        var body = devices.Count == 1
            ? $"{names[0].TrimStart('•', ' ')}\n\n{reasonText}"
            : $"{list}\n\n{reasonText}";

        void Show()
        {
            MessageBox.Show(
                this,
                body,
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        if (InvokeRequired) BeginInvoke(Show);
        else Show();
    }

    private static bool ServerStillHasDevice(OnlineServer server, AttachedUsbDevice attached)
    {
        if (!string.IsNullOrWhiteSpace(attached.BusId) && server.PluggedBusIds.Contains(attached.BusId))
            return true;
        if (!string.IsNullOrWhiteSpace(attached.Vid) && !string.IsNullOrWhiteSpace(attached.Pid) &&
            server.PluggedVidPids.Contains($"{attached.Vid}:{attached.Pid}"))
            return true;
        // Empty list with ReportsPluggedDevices => nothing plugged.
        return false;
    }

    private void PruneStaleServers(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var stale = _onlineServers.Where(kv => kv.Value.LastSeenUtc < cutoff).Select(kv => kv.Key).ToList();
        if (stale.Count == 0) return;

        var typed = _hostBox.Text.Trim();
        var clearedCurrent = stale.Any(h => string.Equals(h, typed, StringComparison.OrdinalIgnoreCase));
        foreach (var h in stale)
            _onlineServers.Remove(h);
        if (clearedCurrent || _onlineServers.Count == 0)
            ClearAvailableDevices();
        RefreshServerListUi();
        if (_onlineServers.Count == 0)
            EnsurePhoneWatch();
    }

    private void RefreshServerListUi()
    {
        var ordered = _onlineServers.Values
            .OrderBy(s => s.Host, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var signature = string.Join("|", ordered.Select(s => $"{s.Host}\u001f{s.Name}"));
        var current = string.Join("|", _serverList.Items.Cast<object>()
            .Select(o => o is OnlineServer s ? $"{s.Host}\u001f{s.Name}" : o?.ToString() ?? ""));
        UpdateDiscoverStatusText();
        if (string.Equals(signature, current, StringComparison.Ordinal))
        {
            UpdateListEmptyVisible(_serverList, _serversEmpty);
            return;
        }

        var selectedHost = (_serverList.SelectedItem as OnlineServer)?.Host;
        _serverList.BeginUpdate();
        _serverList.Items.Clear();
        foreach (var s in ordered)
            _serverList.Items.Add(s);
        _serverList.EndUpdate();
        UpdateListEmptyVisible(_serverList, _serversEmpty);

        if (selectedHost != null)
        {
            for (var i = 0; i < _serverList.Items.Count; i++)
            {
                if (_serverList.Items[i] is OnlineServer s &&
                    string.Equals(s.Host, selectedHost, StringComparison.OrdinalIgnoreCase))
                {
                    _serverList.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    private void StartSearchPulse()
    {
        _searchPulseTimer = new System.Windows.Forms.Timer { Interval = 450 };
        _searchPulseTimer.Tick += (_, _) =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (_onlineServers.Count > 0) return;
            _searchDotPhase = (_searchDotPhase % 3) + 1; // 1..3
            var next = SearchPulseText(_searchDotPhase);
            if (!string.Equals(_connectHero.Headline, next, StringComparison.Ordinal))
                _connectHero.Headline = next;
        };
        _searchPulseTimer.Start();
    }

    private void StartLatencyPing()
    {
        _pingTimer = new System.Windows.Forms.Timer { Interval = 4000 };
        _pingTimer.Tick += async (_, _) => await PingOnlineServersAsync();
        _pingTimer.Start();
        _ = PingOnlineServersAsync();
    }

    /// <summary>
    /// ICMP first; fall back to UDP discovery when ICMP is blocked (common on VPN).
    /// Does not open USB/IP TCP — a raw connect there races with device list/attach.
    /// </summary>
    private static async Task<long?> MeasureRttAsync(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 800).ConfigureAwait(false);
            if (reply.Status == IPStatus.Success)
                return reply.RoundtripTime;
        }
        catch
        {
            // ICMP blocked or unavailable — try discovery UDP below.
        }

        return await LanDiscovery.MeasureRttAsync(host, TimeSpan.FromMilliseconds(800))
            .ConfigureAwait(false);
    }

    private async Task PingOnlineServersAsync()
    {
        if (_pingBusy || IsDisposed || !IsHandleCreated) return;
        var hosts = _onlineServers.Keys.ToList();
        if (hosts.Count == 0) return;

        _pingBusy = true;
        try
        {
            foreach (var host in hosts)
            {
                var rtt = await MeasureRttAsync(host).ConfigureAwait(true);
                if (IsDisposed) return;
                void Apply()
                {
                    if (!_onlineServers.TryGetValue(host, out var server)) return;
                    var before = server.StatusChip + server.StatusLine;
                    server.NotePing(rtt);
                    if (!string.Equals(before, server.StatusChip + server.StatusLine, StringComparison.Ordinal))
                        _serverList.Invalidate();
                }
                if (InvokeRequired) BeginInvoke(Apply);
                else Apply();
            }
        }
        finally
        {
            _pingBusy = false;
        }
    }

    private static string SearchPulseText(int dots)
    {
        dots = Math.Clamp(dots, 1, 3);
        // Pad with spaces so measured width stays stable (avoids layout flicker).
        return "Searching" + new string('.', dots) + new string(' ', 3 - dots);
    }

    private void UpdateDiscoverStatusText()
    {
        string next;
        Color color;
        if (_onlineServers.Count > 0)
        {
            color = OkGreen;
            next = _onlineServers.Count == 1
                ? "Connected"
                : $"{_onlineServers.Count} hosts online";
        }
        else
        {
            color = Accent;
            if (_searchDotPhase < 1) _searchDotPhase = 3;
            next = SearchPulseText(_searchDotPhase);
        }

        if (_connectHero.HeadlineColor != color)
            _connectHero.HeadlineColor = color;
        if (!string.Equals(_connectHero.Headline, next, StringComparison.Ordinal))
            _connectHero.Headline = next;
    }

    private void StartPortPolling()
    {
        _portPollTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _portPollTimer.Tick += async (_, _) => await PollPortStatusAsync();
        _portPollTimer.Start();
    }

    private void StartInventoryPolling()
    {
        _inventoryTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _inventoryTimer.Tick += async (_, _) => await PollInventoryAsync();
        _inventoryTimer.Start();
    }

    private async Task PollPortStatusAsync()
    {
        if (_portPollBusy || _cli == null || !IsHandleCreated || !Visible) return;
        _portPollBusy = true;
        try
        {
            // While a device is live, avoid spawning usbip.exe every 2s — it hitching
            // the VHCI stack shows up as mouse stutter. Just keep the UI honest ~15s.
            // If the phone just stopped, poll immediately so Active clears.
            var expectingStop = _onlineServers.Count == 0 ||
                                _stoppedHostsUntil.Values.Any(t => DateTime.UtcNow < t);
            if (!expectingStop && _attachedCache.Count > 0 &&
                (Environment.TickCount64 / 2000) % 8 != 0)
                return;

            await ApplyPortStatusAsync(updateStatusWhenAttached: false);
        }
        catch { /* ignore */ }
        finally
        {
            _portPollBusy = false;
        }
    }

    private async Task PollInventoryAsync()
    {
        if (_inventoryBusy || !IsHandleCreated || !Visible) return;
        _inventoryBusy = true;
        try
        {
            // During an active import, skip `usbip list -r` (it contends with the URB
            // stream). Still probe UDP so a phone Stop is noticed while a device is Active.
            // If we don't have a discovered server yet, keep doing full Find — leftover
            // usbip ports from auto-connect must not stop discovery.
            if (_attachedCache.Count > 0 && _onlineServers.Count > 0)
            {
                await ApplyPortStatusAsync(updateStatusWhenAttached: false);
                await ProbeServerLivenessAsync();
                return;
            }

            await FindPhoneAsync(autoRefresh: true, quiet: true, fromWatch: true);
            PruneStaleServers(TimeSpan.FromSeconds(20));
        }
        catch { /* ignore background poll exceptions, especially during shutdown */ }
        finally
        {
            _inventoryBusy = false;
        }
    }

    /// <summary>UDP-only check so Stop is noticed even while a device is Active.</summary>
    private async Task ProbeServerLivenessAsync()
    {
        if (_onlineServers.Count == 0) return;
        var hints = new List<string>(_onlineServers.Keys);
        var typed = _hostBox.Text.Trim();
        if (!string.IsNullOrEmpty(typed))
            hints.Add(typed);
        hints.AddRange(_savedHosts);
        var offline = new List<string>();
        IReadOnlyList<LanDiscovery.FoundServer> found;
        try
        {
            found = await LanDiscovery.FindAsync(TimeSpan.FromMilliseconds(600), null, default, hints, offline);
        }
        catch
        {
            return;
        }

        if (offline.Count > 0)
        {
            _livenessMisses = 0;
            await HandlePhoneServerOfflineAsync(new PhoneEventListener.ServerOfflineEvent(offline));
            return;
        }
        if (found.Count > 0)
        {
            _livenessMisses = 0;
            foreach (var s in found)
                RememberServer(s);
            return;
        }

        _livenessMisses++;
        if (_livenessMisses < 2) return;
        _livenessMisses = 0;
        ForgetUnseenServers(found);
        if (_onlineServers.Count == 0)
            EnsurePhoneWatch();
    }

    /// <summary>Read local usbip port state into the Active panel.</summary>
    private async Task<bool> ApplyPortStatusAsync(bool updateStatusWhenAttached)
    {
        if (_mockupMode || _cli == null) return false;
        try
        {
            void Apply(IReadOnlyList<AttachedUsbDevice> attached, string? rawForLog)
            {
                var labeled = attached.Select(d =>
                {
                    var x = WithPhoneLabel(d);
                    return x with { AutoConnect = IsAutoConnectTarget(x) };
                }).ToList();
                var previous = _attachedCache;
                var signature = string.Join("|", labeled.Select(d =>
                    $"{d.Port}\u001f{d.BusId}\u001f{d.RemoteHost}\u001f{d.DisplayName}\u001f{d.Vid}:{d.Pid}\u001f{(d.AutoConnect ? "1" : "0")}"));
                var current = string.Join("|", _attachedList.Items.Cast<object>()
                    .Select(o => o is AttachedUsbDevice a
                        ? $"{a.Port}\u001f{a.BusId}\u001f{a.RemoteHost}\u001f{a.DisplayName}\u001f{a.Vid}:{a.Pid}\u001f{(a.AutoConnect ? "1" : "0")}"
                        : o?.ToString() ?? ""));
                var listUnchanged = string.Equals(signature, current, StringComparison.Ordinal);
                _attachedCache = labeled;

                if (!listUnchanged)
                {
                    var selectedPort = (_attachedList.SelectedItem as AttachedUsbDevice)?.Port;

                    _attachedList.BeginUpdate();
                    _attachedList.Items.Clear();
                    foreach (var d in _attachedCache)
                    {
                        _attachedList.Items.Add(d);
                        if (!string.IsNullOrEmpty(d.RemoteHost) && _onlineServers.Count > 0)
                            RememberServer(d.RemoteHost);
                    }
                    _attachedList.EndUpdate();
                    UpdateListEmptyVisible(_attachedList, _attachedEmpty);
                    UpdateDisconnectUi();

                    if (_attachedCache.Count == 0)
                    {
                        _attachedEmpty.SetCopy("Nothing active", "Right-click devices for more options");
                        if (!string.IsNullOrWhiteSpace(rawForLog) &&
                            rawForLog.Contains("in use", StringComparison.OrdinalIgnoreCase))
                        {
                            _attachedEmpty.SetCopy("Couldn't read active ports", "See Activity for details");
                            Log("Attached device present but parse failed.");
                            Log(rawForLog);
                        }
                    }
                    else
                    {
                        if (selectedPort is int port)
                        {
                            for (var i = 0; i < _attachedList.Items.Count; i++)
                            {
                                if (_attachedList.Items[i] is AttachedUsbDevice a && a.Port == port)
                                {
                                    _attachedList.SelectedIndex = i;
                                    break;
                                }
                            }
                        }
                        if (_attachedList.SelectedIndex < 0)
                            _attachedList.SelectedIndex = 0;
                    }
                }
                else
                {
                    foreach (var d in _attachedCache)
                    {
                        if (!string.IsNullOrEmpty(d.RemoteHost) && _onlineServers.Count > 0)
                            RememberServer(d.RemoteHost);
                    }
                }

                if (_attachedCache.Count > 0)
                {
                    // Prefer the IP usbip actually attached through (often the VPN peer IP).
                    var attachHost = _attachedCache.Select(d => d.RemoteHost)
                        .FirstOrDefault(h => !string.IsNullOrWhiteSpace(h));
                    if (!string.IsNullOrWhiteSpace(attachHost) &&
                        !string.Equals(_hostBox.Text.Trim(), attachHost, StringComparison.OrdinalIgnoreCase))
                    {
                        _hostBox.Text = attachHost;
                        SaveHost(attachHost!);
                    }

                    if (updateStatusWhenAttached)
                    {
                        var first = _attachedCache[0];
                        var label = first.Vid != null && first.Pid != null
                            ? $"{first.DisplayName} ({first.Vid}:{first.Pid})"
                            : first.DisplayName;
                        SetStatus($"Connected — {label}", OkGreen);
                        // Do not touch the connect hint here — port/inventory polls run often and would flicker it.
                    }
                    UpdateDisconnectUi();
                }

                // Keep Available exclusive of Active. Do not bounce a just-dropped
                // Active device back into Available from the stale remote cache
                // (that is what made Stop look like the USB device "came back" for ~10s).
                var host = _hostBox.Text.Trim();
                var dropped = previous.Where(p => labeled.All(a => a.Port != p.Port)).ToList();
                if (dropped.Count > 0 && _remoteCache.Count > 0)
                {
                    _remoteCache = _remoteCache
                        .Where(r => !dropped.Any(d => IsAttachedMatch(r, d, host)))
                        .ToList();
                }
                if (_remoteCache.Count > 0)
                    PopulateAvailableDevices(_remoteCache, host);
                else if (dropped.Count > 0)
                    ClearAvailableDevices();
            }

            if (InvokeRequired)
            {
                var raw = await _cli.PortStatusAsync().ConfigureAwait(true);
                var attached = UsbipCli.ParseAttachedDevices(raw);
                BeginInvoke(() => Apply(attached, raw));
                return attached.Count > 0;
            }

            {
                var raw = await _cli.PortStatusAsync();
                var attached = UsbipCli.ParseAttachedDevices(raw);
                Apply(attached, raw);
                return attached.Count > 0;
            }
        }
        catch (Exception ex)
        {
            if (IsHandleCreated)
            {
                void ShowErr()
                {
                    _attachedEmpty.SetCopy("Port status error", ex.Message);
                    _attachedEmpty.Visible = true;
                    _attachedEmpty.BringToFront();
                }
                if (InvokeRequired) BeginInvoke(ShowErr);
                else ShowErr();
            }
            Log("Port status failed: " + ex.Message);
            return false;
        }
    }

    private async Task OnFirstShownAsync()
    {
        if (_mockupMode || _autoFindStarted) return;
        _autoFindStarted = true;
        // Show any already-attached device immediately (before discovery).
        await ApplyPortStatusAsync(updateStatusWhenAttached: true);
        Log("Looking for a USB host on the network…");
        // FindPhoneAsync starts background watch if the phone isn't up yet.
        await FindPhoneAsync(autoRefresh: true, quiet: true);
        if (_onlineServers.Count == 0)
            EnsurePhoneWatch();
        if (_startInTray)
        {
            _trayTipShown = true;
            HideToTray();
        }
    }

    /// <summary>
    /// Keep probing until the phone starts advertising (e.g. Windows app opened first).
    /// </summary>
    private void EnsurePhoneWatch()
    {
        if (_phoneWatchCts != null) return;
        StartPhoneWatch();
    }

    private void StartPhoneWatch()
    {
        StopPhoneWatch();
        _phoneWatchCts = new CancellationTokenSource();
        var ct = _phoneWatchCts.Token;
        SetStatus("Waiting for a USB host on the network…", Accent);
        if (!_loggedWaitingForPhone)
        {
            _connectHero.Hint = "Start UsbNetBridge on your device and tap Start — this PC will find it automatically.";
            _loggedWaitingForPhone = true;
            Log("No USB host yet — will keep looking in the background.");
        }
        _ = WatchPhoneAsync(ct);
    }

    private void StopPhoneWatch()
    {
        try { _phoneWatchCts?.Cancel(); } catch { /* ignore */ }
        _phoneWatchCts?.Dispose();
        _phoneWatchCts = null;
    }

    private async Task WatchPhoneAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var found = await FindPhoneAsync(
                    autoRefresh: true,
                    quiet: true,
                    fromWatch: true,
                    ct: ct);
                if (found)
                    break;
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // stopped
        }
    }

    internal void ListenForActivation(EventWaitHandle showRequested)
    {
        _showRequested = showRequested;
        var thread = new Thread(ShowRequestLoop)
        {
            IsBackground = true,
            Name = "UsbNetBridge.SingleInstance",
        };
        thread.Start();
    }

    private void ShowRequestLoop()
    {
        var ev = _showRequested;
        if (ev == null)
            return;
        try
        {
            while (ev.WaitOne())
            {
                if (IsDisposed)
                    break;
                try
                {
                    if (IsHandleCreated)
                        BeginInvoke(RestoreFromTray);
                    else
                        Shown += OnShownRestoreOnce;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // EventWaitHandle closed as the process exits.
        }
    }

    private void OnShownRestoreOnce(object? sender, EventArgs e)
    {
        Shown -= OnShownRestoreOnce;
        RestoreFromTray();
    }

    private NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open UsbNetBridge", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Find USB host", null, async (_, _) =>
        {
            RestoreFromTray();
            await FindPhoneAsync(autoRefresh: true, quiet: false);
        });
        menu.Items.Add("Disconnect all\tCtrl+Alt+D", null, async (_, _) => await DetachAllAsync());
        menu.Items.Add("Reconnect\tCtrl+Alt+A", null, async (_, _) => await ReconnectHotkeyAsync());
        menu.Items.Add(new ToolStripSeparator());
        _alwaysTrayMenuItem = new ToolStripMenuItem("Always minimize to tray")
        {
            CheckOnClick = true,
            Checked = _alwaysMinimizeToTray,
        };
        _alwaysTrayMenuItem.CheckedChanged += (_, _) =>
        {
            if (_alwaysMinimizeToTray == _alwaysTrayMenuItem.Checked)
                return;
            _alwaysMinimizeToTray = _alwaysTrayMenuItem.Checked;
            SaveAlwaysMinimizeToTray(_alwaysMinimizeToTray);
        };
        menu.Items.Add(_alwaysTrayMenuItem);
        _startWithWindowsItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = LoadStartWithWindows(),
        };
        _startWithWindowsItem.CheckedChanged += (_, _) =>
        {
            try { SaveStartWithWindows(_startWithWindowsItem.Checked); }
            catch (Exception ex) { Log("Could not change Start with Windows: " + ex.Message); }
        };
        menu.Items.Add(_startWithWindowsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await ExitFromTrayAsync());

        var tray = new NotifyIcon
        {
            Text = AppTitle,
            Icon = Icon ?? SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu,
            BalloonTipTitle = AppTitle,
        };
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                RestoreFromTray();
        };
        tray.DoubleClick += (_, _) => RestoreFromTray();
        return tray;
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var associated = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (associated != null) return associated;
        }
        catch { /* ignore */ }

        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(icoPath)) return new Icon(icoPath);
        }
        catch { /* ignore */ }

        return SystemIcons.Application;
    }

    private static Image? LoadTitleLogo()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "title-logo.png");
            if (File.Exists(path))
                return Image.FromFile(path);
        }
        catch { /* ignore */ }
        return null;
    }

    private void RefreshDriverStatus()
    {
        var path = UsbipCli.FindUsbipExe();
        if (path == null)
        {
            _cli = null;
            SetStatus("Setup needed — install usbip-win2 first", Danger);
            _connectHero.Hint = "Install usbip-win2, then restart this app. See windows/INSTALL_USBIP_WIN2.md";
            _refreshBtn.Enabled = false;
            return;
        }

        try
        {
            _cli = new UsbipCli(path);
            SetStatus("Ready — find a USB host, then pick a USB device", OkGreen);
            _connectHero.Hint = "";
            _refreshBtn.Enabled = true;
            UpdateDisconnectUi();
        }
        catch (Exception ex)
        {
            _cli = null;
            SetStatus(ex.Message, Danger);
        }
    }

    private async Task<bool> FindPhoneAsync(
        bool autoRefresh,
        bool quiet,
        bool fromWatch = false,
        CancellationToken ct = default)
    {
        try
        {
            if (!await _findLock.WaitAsync(TimeSpan.FromSeconds(8), ct))
                return false;
        }
        catch { return false; }

        try
        {
            if (!fromWatch)
                _findBtn.Enabled = false;
            if (!quiet)
            {
                UseWaitCursor = true;
                SetStatus("Searching the network for a USB host…", Accent);
            }

            IProgress<string>? progress = fromWatch ? null : new Progress<string>(Log);
            var timeout = fromWatch ? TimeSpan.FromSeconds(2.5) : TimeSpan.FromSeconds(4);
            // Unicast to typed/saved IP — required over VPN where broadcasts are dropped.
            var hints = new List<string>();
            var typed = _hostBox.Text.Trim();
            if (!string.IsNullOrEmpty(typed))
                hints.Add(typed);
            hints.AddRange(_savedHosts);
            hints.AddRange(_onlineServers.Keys);
            try
            {
                var last = File.ReadAllText(HostSettingsPath()).Trim();
                if (!string.IsNullOrEmpty(last))
                    hints.Add(last);
            }
            catch { /* ignore */ }

            var offlineHosts = new List<string>();
            var servers = (await LanDiscovery.FindAsync(timeout, progress, ct, hints, offlineHosts)).ToList();
            if (offlineHosts.Count > 0)
                await HandlePhoneServerOfflineAsync(new PhoneEventListener.ServerOfflineEvent(offlineHosts));

            if (servers.Count == 0)
            {
                if (!fromWatch)
                    Log("No USB host found.");
                // Leftover usbip ports (common after auto-connect) are not a found phone.
                if (_cli != null)
                    await ApplyPortStatusAsync(updateStatusWhenAttached: false);
                if (_onlineServers.Count == 0)
                {
                    if (quiet)
                        SetStatus("Waiting for a USB host on the network…", Accent);
                    EnsurePhoneWatch();
                }
                else if (!quiet)
                {
                    SetStatus("No USB host found on the network", Danger);
                    MessageBox.Show(this,
                        "Couldn't find a USB host.\n\n" +
                        "• Open UsbNetBridge on the USB host and tap Start\n" +
                        "• Use the same network (Wi‑Fi, hotspot, or VPN)\n" +
                        "• Allow Windows Firewall if asked\n\n" +
                        "This app will keep looking automatically.",
                        "Find USB host",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    StartPhoneWatch();
                }
                return false;
            }

            LanDiscovery.FoundServer chosen;
            if (servers.Count == 1 || quiet)
            {
                chosen = servers[0];
                if (servers.Count > 1)
                    Log($"Several USB hosts found — using {chosen.Host}.");
            }
            else
            {
                var labels = servers.Select(s => $"{s.Host}  ({s.Name})").ToArray();
                using var pick = new Form
                {
                    Text = "Choose USB host",
                    Width = 420,
                    Height = 280,
                    StartPosition = FormStartPosition.CenterParent,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    BackColor = BgMid,
                    ForeColor = TextPrimary,
                };
                var list = new ListBox
                {
                    Dock = DockStyle.Fill,
                    IntegralHeight = false,
                    BackColor = InputBg,
                    ForeColor = TextPrimary,
                    Font = new Font("Segoe UI", 11f),
                };
                list.Items.AddRange(labels);
                list.SelectedIndex = 0;
                var ok = new Soft3dButton { DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 44 };
                StylePrimaryButton(ok, "Use this device");
                ok.Margin = new Padding(12, 8, 12, 12);
                pick.Controls.Add(list);
                pick.Controls.Add(ok);
                pick.AcceptButton = ok;
                if (pick.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0)
                {
                    Log("Find cancelled.");
                    StartPhoneWatch();
                    return false;
                }
                chosen = servers[list.SelectedIndex];
            }

            _hostBox.Text = chosen.Host;
            SaveHost(chosen.Host);
            RememberSavedHost(chosen.Host);
            RememberServer(chosen);
            foreach (var s in servers)
                RememberServer(s);
            _ = TryAutoConnectFromDiscoveryAsync(servers);
            // Refresh Active before pruning ghosts (inventory poll may not have run yet).
            if (_cli != null)
                await ApplyPortStatusAsync(updateStatusWhenAttached: false);
            await DetachStaleAttachesAsync();
                Log($"USB host found: {chosen.Host}");
            SetStatus($"USB host found — {chosen.Host}", OkGreen);
            if (!quiet)
                _connectHero.Hint = "Double-click to connect. Right-click for options.";

            var refreshed = true;
            if (autoRefresh && _cli != null)
                refreshed = await RefreshDevicesAsync(quiet: true);

            // Remote list can fail once the device is already imported — still treat
            // a live local usbip port as success so the UI shows Connected.
            if (!refreshed && _cli != null && await ApplyPortStatusAsync(updateStatusWhenAttached: false))
                refreshed = true;

            if (_onlineServers.Count > 0)
                StopPhoneWatch();
            else
                EnsurePhoneWatch();

            return _onlineServers.Count > 0;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Log("Find failed: " + ex.Message);
            SetStatus("Find failed", Danger);
            if (!quiet)
                MessageBox.Show(this, ex.Message, "Find USB host", MessageBoxButtons.OK, MessageBoxIcon.Error);
            if (quiet || fromWatch)
                StartPhoneWatch();
            return false;
        }
        finally
        {
            UseWaitCursor = false;
            _findBtn.Enabled = true;
            try { _findLock.Release(); } catch { /* ignore */ }
        }
    }

    private async Task RefreshDevicesAsync() => await RefreshDevicesAsync(quiet: false);

    private async Task<bool> RefreshDevicesAsync(bool quiet)
    {
        if (_cli == null)
        {
            RefreshDriverStatus();
            if (_cli == null)
            {
                if (!quiet)
                    MessageBox.Show(this, "Install usbip-win2 first, then restart this app.", "Setup needed");
                return false;
            }
        }

        var host = _hostBox.Text.Trim();
        if (string.IsNullOrEmpty(host))
        {
            return await FindPhoneAsync(autoRefresh: true, quiet: quiet);
        }
        if (IsRecentlyStopped(host))
        {
            ClearAvailableDevices();
            return false;
        }

        try
        {
            if (!quiet)
                UseWaitCursor = true;

            // Always refresh Active from local usbip ports first. The mouse is often
            // already imported while Devices looks empty (VPN IP vs Wi‑Fi IP).
            await ApplyPortStatusAsync(updateStatusWhenAttached: false);
            if (_attachedCache.Count > 0)
            {
                var advertised = _onlineServers.Values.SelectMany(s => s.AdvertisedDevices).ToList();
                if (advertised.Count > 0)
                    PopulateAvailableDevices(advertised, host);
                else if (_remoteCache.Count > 0)
                    PopulateAvailableDevices(_remoteCache, host);
                Log($"Already connected — {_attachedCache.Count} active device(s).");
                return true;
            }

            SetStatus("Loading devices from the USB host…", Accent);
            Log($"Loading devices from {host}…");
            IReadOnlyList<RemoteUsbDevice>? devices = null;
            Exception? lastListError = null;
            var hostsToTry = new List<string> { host };
            foreach (var other in _onlineServers.Keys)
            {
                if (!hostsToTry.Contains(other, StringComparer.OrdinalIgnoreCase))
                    hostsToTry.Add(other);
            }

            foreach (var tryHost in hostsToTry)
            {
                if (IsRecentlyStopped(tryHost)) continue;
                try
                {
                    devices = await _cli.ListRemoteAsync(tryHost);
                    if (!string.Equals(tryHost, host, StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"Listed devices via {tryHost} (typed address {host} did not answer).");
                        _hostBox.Text = tryHost;
                        host = tryHost;
                    }
                    lastListError = null;
                    break;
                }
                catch (Exception ex)
                {
                    lastListError = ex;
                }
            }

            if (devices == null)
            {
                var advertised = _onlineServers.Values.SelectMany(s => s.AdvertisedDevices).ToList();
                if (advertised.Count > 0)
                {
                    Log("USB/IP list failed — showing devices advertised by the USB host.");
                    devices = advertised;
                }
                else if (lastListError != null)
                    throw lastListError;
                else
                    devices = Array.Empty<RemoteUsbDevice>();
            }

            RememberServer(host);
            // Refresh attached first so Available can exclude Active devices.
            await ApplyPortStatusAsync(updateStatusWhenAttached: false);
            PopulateAvailableDevices(devices, host);
            if (devices.Count == 0)
                Log($"No exportable devices on {host}.");
            else
            {
                var shown = _deviceList.Items.Count;
                var hidden = devices.Count - shown;
                Log(hidden > 0
                    ? $"Found {devices.Count} device(s) on {host} ({hidden} already active)."
                    : $"Found {devices.Count} device(s) on {host}.");
            }
            SaveHost(host);

            var attached = _attachedCache.Count > 0;
            if (!attached)
            {
                SetStatus(_deviceList.Items.Count > 0
                    ? $"Server {host} — select a device"
                    : $"Server {host} online — no free devices", OkGreen);
                if (!quiet)
                    _connectHero.Hint = "Double-click to connect. Right-click for options.";
            }
            return true;
        }
        catch (Exception ex)
        {
            _remoteCache = Array.Empty<RemoteUsbDevice>();
            _deviceList.Items.Clear();
            UpdateDevicesEmptyVisible();
            Log("Refresh failed: " + ex.Message);

            // Already attached devices often disappear from the phone's export list.
            if (await ApplyPortStatusAsync(updateStatusWhenAttached: false))
                return true;

            SetStatus(quiet ? "Waiting for a USB host on the network…" : "Couldn't load devices", quiet ? Accent : Danger);
            if (!quiet)
                MessageBox.Show(this, ex.Message, "Refresh failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private async Task AttachSelectedAsync()
    {
        if (_cli == null || _deviceList.SelectedItem is not RemoteUsbDevice device)
        {
            MessageBox.Show(this, "Double-click a device in Devices first.");
            return;
        }

        await AttachDeviceAsync(device, interactive: true);
    }

    private async Task AttachDeviceAsync(RemoteUsbDevice device, bool interactive)
    {
        var host = _hostBox.Text.Trim();
        if (string.IsNullOrEmpty(host))
        {
            if (interactive)
                MessageBox.Show(this, "Find a USB host first.");
            return;
        }
        if (_onlineServers.TryGetValue(host, out var busy) && busy.IsBusyElsewhere)
        {
            var who = string.IsNullOrWhiteSpace(busy.BusyPc) ? "another PC" : busy.BusyPc;
            var msg = $"This USB host is already in use on {who}.";
            Log(msg);
            SetStatus("In use on another PC", Danger);
            if (interactive)
                MessageBox.Show(this, msg + "\n\nDetach there first, or tap Disconnect all on that PC (Ctrl+Alt+D).",
                    "Already in use", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            if (interactive)
                UseWaitCursor = true;
            SetStatus("Connecting device to this PC…", Accent);
            Log(interactive ? "Preparing..." : $"Auto-connecting {device.DisplayName}...");

            Log($"Connecting {device.DisplayName}...");
            RememberPhoneLabel(device.BusId, device.Vid, device.Pid, device.DisplayName);
            await _cli!.AttachAsync(host, device.BusId);
            await ApplyPortStatusAsync(updateStatusWhenAttached: true);
            if (!_attachedCache.Any(a => IsAttachedMatch(device, a, host)))
            {
                await Task.Delay(400);
                await ApplyPortStatusAsync(updateStatusWhenAttached: true);
            }
            if (_remoteCache.Count > 0)
                PopulateAvailableDevices(_remoteCache, host);

            Log("Connected. You can close this window — it stays in the tray.");
            SetStatus("Connected — device ready on this PC", OkGreen);
            _connectHero.Hint = "";
            
            if (interactive)
            {
                var tipPath = Path.Combine(Path.GetDirectoryName(AutoConnectPath())!, "has_seen_menu_tip.txt");
                if (!File.Exists(tipPath))
                {
                    File.WriteAllText(tipPath, "1");
                    MessageBox.Show(this, "Device connected!\n\nTip: You can double-click or right-click devices to disconnect them, or right-click available devices to set up Auto-Connect.", "Tip", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            SaveHost(host);
            StopPhoneWatch();
        }
        catch (Exception ex)
        {
            var isPermissionDenied = ex.Message.Contains("Device not available", StringComparison.OrdinalIgnoreCase);

            if (isPermissionDenied && !interactive)
            {
                var key = AutoConnectKey(device.Vid, device.Pid, device.BusId);
                if (key.Length > 0)
                    _autoConnectHoldoff.Add(key);
            }

            Log(isPermissionDenied ? "Connect failed: Waiting for host permission." : "Connect failed: " + ex.Message);
            SetStatus(isPermissionDenied ? "Waiting for permission..." : "Connect failed", isPermissionDenied ? Accent : Danger);
            
            if (interactive)
            {
                if (isPermissionDenied)
                {
                    MessageBox.Show(this, "This device isn't ready yet.\n\nPlease check your phone and tap 'Allow' so the app can share this specific USB device.", "Permission Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    var extra = "";
                    if (_onlineServers.TryGetValue(host, out var srv) && srv.IsBusyElsewhere)
                    {
                        var who = string.IsNullOrWhiteSpace(srv.BusyPc) ? "another PC" : srv.BusyPc;
                        extra = $"\n\nThis USB host is already in use on {who}.";
                    }
                    MessageBox.Show(this, ex.Message + extra, "Connect failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        finally
        {
            if (interactive)
                UseWaitCursor = false;
        }
    }

    private void ServerListOnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        var idx = _serverList.IndexFromPoint(e.Location);
        if (idx < 0) return;
        _serverList.SelectedIndex = idx;
        if (_serverList.Items[idx] is not OnlineServer s) return;
        ShowAutoConnectMenu(_serverList, e.Location, s.Name ?? s.Host, null, null, null, false, null);
    }

    private void DeviceListOnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        var idx = _deviceList.IndexFromPoint(e.Location);
        if (idx < 0) return;
        _deviceList.SelectedIndex = idx;
        if (_deviceList.Items[idx] is not RemoteUsbDevice device) return;
        ShowAutoConnectMenu(_deviceList, e.Location, device.DisplayName, device.Vid, device.Pid, device.BusId,
            alreadyAvailable: true, device);
    }

    private void AttachedListOnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        var idx = _attachedList.IndexFromPoint(e.Location);
        if (idx < 0) return;
        _attachedList.SelectedIndex = idx;
        if (_attachedList.Items[idx] is not AttachedUsbDevice device) return;
        ShowAutoConnectMenu(_attachedList, e.Location, device.DisplayName, device.Vid, device.Pid, device.BusId,
            alreadyAvailable: false, remote: null);
    }

    private void ShowAutoConnectMenu(
        Control owner, Point location, string name,
        string? vid, string? pid, string? busId,
        bool alreadyAvailable, RemoteUsbDevice? remote)
    {
        _autoConnectMenu?.Dispose();
        var menu = new ContextMenuStrip();
        _autoConnectMenu = menu;

        var isAttached = owner == _attachedList;
        var isServer = owner == _serverList;
        if (isAttached)
        {
            menu.Items.Add($"Disconnect {name}", null, (_, _) =>
            {
                BeginInvoke(new Action(() => _ = DetachSelectedAsync()));
            });
            menu.Items.Add("Disconnect all", null, (_, _) =>
            {
                BeginInvoke(new Action(() => _ = DetachAllAsync()));
            });
            menu.Items.Add("-");
        }

        if (!isServer)
        {
            var isTarget = IsAutoConnectTarget(vid, pid, busId);
            if (isTarget)
            {
            menu.Items.Add($"Don’t auto-connect {name}", null, (_, _) => ClearAutoConnect());
        }
        else
        {
            var toAttach = remote;
            menu.Items.Add($"Always connect {name}", null, (_, _) =>
            {
                EnableAutoConnect(vid, pid, busId, name);
                if (!alreadyAvailable || toAttach == null || _cli == null) return;
                var device = toAttach with { AutoConnect = true };
                // Run after the menu finishes closing — disposing/awaiting during Click
                // hits "Cannot access a disposed object (ContextMenuStrip)".
                BeginInvoke(new Action(() => _ = AttachDeviceAsync(device, interactive: true)));
            });
        }
        }

        menu.Items.Add("-");
        menu.Items.Add(isServer ? "Host info..." : "Device info...", null, (_, _) =>
        {
            var parts = new List<string> { $"Name: {name}" };
            if (!string.IsNullOrWhiteSpace(vid) && !string.IsNullOrWhiteSpace(pid))
                parts.Add($"Hardware ID: {vid}:{pid}");
            if (!string.IsNullOrWhiteSpace(busId))
                parts.Add($"Bus ID: {busId}");
            if (isServer && _serverList.SelectedItem is OnlineServer s)
            {
                parts.Add($"Host IP: {s.Host}");
                if (!string.IsNullOrWhiteSpace(s.StatusLine))
                    parts.Add($"Status: {s.StatusLine}");
            }
            if (owner == _attachedList && _attachedList.SelectedItem is AttachedUsbDevice attached)
            {
                if (!string.IsNullOrWhiteSpace(attached.RemoteHost))
                    parts.Add($"Host IP: {attached.RemoteHost}");
                parts.Add($"Port: {attached.Port:D2}");
            }
            MessageBox.Show(this, string.Join("\n", parts), isServer ? "Host Info" : "Device Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });

        menu.Show(owner, location);
    }

    private void EnableAutoConnect(string? vid, string? pid, string? busId, string name)
    {
        _autoConnect = new AutoConnectPref(vid, pid, busId, name.Trim());
        var key = AutoConnectKey(vid, pid, busId);
        if (key.Length > 0)
            _autoConnectHoldoff.Remove(key);
        SaveAutoConnect();
        RefreshAutoConnectChips();
        Log($"Will auto-connect {name} whenever it appears.");
        _connectHero.Hint = $"Auto-connect is on for {name}. Right-click for options.";
    }

    private void ClearAutoConnect()
    {
        var label = _autoConnect?.Label;
        _autoConnect = null;
        SaveAutoConnect();
        RefreshAutoConnectChips();
        Log(string.IsNullOrWhiteSpace(label) ? "Auto-connect off." : $"Auto-connect off for {label}.");
        _connectHero.Hint = "Double-click to connect. Right-click for options.";
    }

    private void RefreshAutoConnectChips()
    {
        _attachedCache = _attachedCache
            .Select(d => d with { AutoConnect = IsAutoConnectTarget(d) })
            .ToList();
        for (var i = 0; i < _attachedList.Items.Count; i++)
        {
            if (_attachedList.Items[i] is AttachedUsbDevice a)
                _attachedList.Items[i] = a with { AutoConnect = IsAutoConnectTarget(a) };
        }
        _attachedList.Invalidate();
        _deviceList.Invalidate();
    }

    private bool IsAutoConnectTarget(RemoteUsbDevice d) =>
        IsAutoConnectTarget(d.Vid, d.Pid, d.BusId);

    private bool IsAutoConnectTarget(AttachedUsbDevice d) =>
        IsAutoConnectTarget(d.Vid, d.Pid, d.BusId);

    private bool IsAutoConnectTarget(string? vid, string? pid, string? busId)
    {
        if (_autoConnect == null) return false;
        if (!string.IsNullOrWhiteSpace(_autoConnect.Vid) && !string.IsNullOrWhiteSpace(_autoConnect.Pid) &&
            !string.IsNullOrWhiteSpace(vid) && !string.IsNullOrWhiteSpace(pid) &&
            string.Equals(_autoConnect.Vid, vid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_autoConnect.Pid, pid, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(_autoConnect.BusId) && !string.IsNullOrWhiteSpace(busId) &&
            string.Equals(_autoConnect.BusId, busId, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static string AutoConnectKey(string? vid, string? pid, string? busId)
    {
        if (!string.IsNullOrWhiteSpace(vid) && !string.IsNullOrWhiteSpace(pid))
            return $"{vid}:{pid}";
        return busId?.Trim() ?? "";
    }

    private void MaybeAutoConnectFromAvailable(IReadOnlyList<RemoteUsbDevice> available)
    {
        try
        {
            if (_autoConnect == null || _cli == null || _autoConnectBusy) return;
            if (_onlineServers.Count == 0) return;
            if (_attachedCache.Any(IsAutoConnectTarget)) return;
            var match = available.FirstOrDefault(IsAutoConnectTarget);
            if (match == null) return;
            var key = AutoConnectKey(match.Vid, match.Pid, match.BusId);
            if (key.Length == 0 || _autoConnectHoldoff.Contains(key)) return;
            _ = TryAutoConnectAsync(match);
        }
        catch (Exception ex)
        {
            Log("Auto-connect skipped: " + ex.Message);
        }
    }

    private void PruneAutoConnectHoldoff(IEnumerable<RemoteUsbDevice> available)
    {
        // To prevent spam loops from UDP broadcast drops (where Android temporarily drops off
        // the network and comes back, triggering Auto-Connect repeatedly for a rejected device),
        // we deliberately do NOT prune the holdoff list based on network visibility.
        // Once a device is rejected, it stays in the holdoff list until the app restarts
        // or the user manually attempts to connect.
    }

    private async Task TryAutoConnectFromDiscoveryAsync(IEnumerable<LanDiscovery.FoundServer> servers)
    {
        if (_autoConnect == null || _cli == null || _autoConnectBusy) return;
        if (_attachedCache.Any(IsAutoConnectTarget)) return;
        foreach (var server in servers)
        {
            foreach (var d in server.PluggedDevices)
            {
                if (!IsAutoConnectTarget(d.Vid, d.Pid, d.BusId) || string.IsNullOrWhiteSpace(d.BusId))
                    continue;
                var key = AutoConnectKey(d.Vid, d.Pid, d.BusId);
                if (key.Length == 0 || _autoConnectHoldoff.Contains(key))
                    return;
                var remote = new RemoteUsbDevice(
                    d.BusId,
                    d.Label ?? "USB device",
                    d.Vid,
                    d.Pid,
                    d.Label,
                    AutoConnect: true);
                await TryAutoConnectAsync(remote);
                return;
            }
        }
    }

    private async Task TryAutoConnectAsync(RemoteUsbDevice device)
    {
        if (_autoConnectBusy) return;
        _autoConnectBusy = true;
        try
        {
            await AttachDeviceAsync(device, interactive: false);
        }
        finally
        {
            _autoConnectBusy = false;
        }
    }

    private void LoadAutoConnect()
    {
        _autoConnect = null;
        try
        {
            var path = AutoConnectPath();
            if (!File.Exists(path)) return;
            var line = File.ReadAllLines(path).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
            if (string.IsNullOrEmpty(line)) return;
            // vid:pid|busId|label
            var parts = line.Split('|');
            if (parts.Length < 3) return;
            string? vid = null, pid = null;
            var id = parts[0].Trim();
            var colon = id.IndexOf(':');
            if (colon > 0)
            {
                vid = id[..colon];
                pid = id[(colon + 1)..];
            }
            var busId = parts[1].Trim();
            var label = parts.Length > 2 ? string.Join("|", parts.Skip(2)).Trim() : "USB device";
            if (string.IsNullOrEmpty(label)) label = "USB device";
            if (string.IsNullOrEmpty(vid) && string.IsNullOrEmpty(busId)) return;
            _autoConnect = new AutoConnectPref(vid, pid, string.IsNullOrEmpty(busId) ? null : busId, label);
        }
        catch { /* ignore */ }
    }

    private void SaveAutoConnect()
    {
        try
        {
            var path = AutoConnectPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (_autoConnect == null)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            var id = !string.IsNullOrWhiteSpace(_autoConnect.Vid) && !string.IsNullOrWhiteSpace(_autoConnect.Pid)
                ? $"{_autoConnect.Vid}:{_autoConnect.Pid}"
                : "";
            File.WriteAllText(path, $"{id}|{_autoConnect.BusId}|{_autoConnect.Label}");
        }
        catch { /* ignore */ }
    }

    private async Task DetachSelectedAsync()
    {
        if (_cli == null)
        {
            MessageBox.Show(this, "Install usbip-win2 first.");
            return;
        }
        if (_attachedList.SelectedItem is not AttachedUsbDevice device)
        {
            MessageBox.Show(this, "Select an active device to disconnect.");
            return;
        }

        try
        {
            UseWaitCursor = true;
            _suppressGoneUntilUtc = DateTime.UtcNow.AddSeconds(4);
            SetStatus($"Disconnecting {device.DisplayName}…", Accent);
            Log($"Disconnecting {device.DisplayName} (port {device.Port:D2})…");
            var key = AutoConnectKey(device.Vid, device.Pid, device.BusId);
            if (key.Length > 0)
                _autoConnectHoldoff.Add(key);
            await _cli.DetachAsync(device.Port);
            await ApplyPortStatusAsync(updateStatusWhenAttached: false);
            if (_attachedCache.Count == 0)
            {
                SetStatus("Ready — find a USB host, then pick a USB device", OkGreen);
                _connectHero.Hint = "Double-click to connect. Right-click for options.";
            }
            await RefreshDevicesAsync(quiet: true);
            Log("Disconnected.");
            ShowDeviceDisconnectedPopup(new[] { device }, DeviceGoneReason.LocalDisconnect);
            UpdateDisconnectUi();
        }
        catch (Exception ex)
        {
            Log("Disconnect failed: " + ex.Message);
            SetStatus("Couldn't disconnect device", Danger);
            MessageBox.Show(this, ex.Message, "Disconnect failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            UpdateDisconnectUi();
        }
    }

    private async Task DetachAllAsync()
    {
        if (_cli == null)
        {
            MessageBox.Show(this, "Install usbip-win2 first.");
            return;
        }
        try
        {
            UseWaitCursor = true;
            _suppressGoneUntilUtc = DateTime.UtcNow.AddSeconds(4);
            SetStatus("Disconnecting all devices…", Accent);
            Log("Disconnecting all devices…");
            foreach (var a in _attachedCache)
            {
                var key = AutoConnectKey(a.Vid, a.Pid, a.BusId);
                if (key.Length > 0)
                    _autoConnectHoldoff.Add(key);
            }
            await _cli.DetachAllAsync();
            await ApplyPortStatusAsync(updateStatusWhenAttached: false);
            Log("All devices disconnected.");
            SetStatus("Ready — find a USB host, then pick a USB device", OkGreen);
            _connectHero.Hint = "";
            _devicesEmpty.SetCopy("No devices yet", "");
            await RefreshDevicesAsync(quiet: true);
        }
        catch (Exception ex)
        {
            Log("Disconnect all failed: " + ex.Message);
            SetStatus("Couldn't disconnect all devices", Danger);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_forceExit ||
            e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing or CloseReason.ApplicationExitCall)
        {
            base.OnFormClosing(e);
            return;
        }

        e.Cancel = true;
        if (_closePromptOpen || _exiting)
            return;

        if (_alwaysMinimizeToTray)
        {
            HideToTray();
            base.OnFormClosing(e);
            return;
        }

        _closePromptOpen = true;
        try
        {
            var choice = AskCloseChoice(out var rememberTray);
            if (choice == CloseChoice.Tray)
            {
                if (rememberTray)
                    SetAlwaysMinimizeToTray(true);
                HideToTray();
            }
            else if (choice == CloseChoice.Exit)
            {
                _ = ExitFromTrayAsync();
            }
        }
        finally
        {
            _closePromptOpen = false;
        }

        base.OnFormClosing(e);
    }

    private enum CloseChoice { Cancel, Tray, Exit }

    private CloseChoice AskCloseChoice(out bool alwaysTray)
    {
        alwaysTray = false;
        using var dlg = new Form
        {
            Text = "Close UsbNetBridge",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            AutoScaleMode = AutoScaleMode.None,
            BackColor = BgMid,
            ForeColor = TextPrimary,
            Font = Font,
            ClientSize = new Size((int)(460 * UiScale), (int)(250 * UiScale)),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 16, 18, 14),
            BackColor = BgMid,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40 * UiScale));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var copy = new Label
        {
            Text = "Close the window, or keep UsbNetBridge running in the tray?\n\nConnected USB devices stay attached if you minimize to tray.",
            AutoSize = true,
            MaximumSize = new Size((int)(410 * UiScale), 0),
            ForeColor = TextPrimary,
            BackColor = BgMid,
            Margin = new Padding(0, 0, 0, 8),
        };

        var remember = new CheckBox
        {
            Text = "Always minimize to tray",
            AutoSize = false,
            Dock = DockStyle.Fill,
            AutoCheck = true,
            FlatStyle = FlatStyle.Standard,
            BackColor = BgMid,
            ForeColor = TextPrimary,
            UseVisualStyleBackColor = false,
            CheckAlign = ContentAlignment.MiddleLeft,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
            Margin = new Padding(0, 4, 0, 8),
            Cursor = Cursors.Hand,
            TabStop = true,
        };

        var trayBtn = new Soft3dButton { DialogResult = DialogResult.OK };
        StylePrimaryButton(trayBtn, "Minimize to tray");
        trayBtn.Margin = new Padding(0, 0, 10, 0);

        var exitBtn = new Soft3dButton { DialogResult = DialogResult.Abort };
        StyleSecondaryButton(exitBtn, "Exit");

        var dismiss = new Button
        {
            DialogResult = DialogResult.Cancel,
            TabStop = false,
            Visible = false,
        };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = BgMid,
            Margin = new Padding(0, 4, 0, 0),
            Padding = new Padding(0),
        };
        buttons.Controls.Add(trayBtn);
        buttons.Controls.Add(exitBtn);

        layout.Controls.Add(copy, 0, 0);
        layout.Controls.Add(remember, 0, 1);
        layout.Controls.Add(buttons, 0, 2);

        dlg.Controls.Add(dismiss);
        dlg.Controls.Add(layout);
        dlg.AcceptButton = trayBtn;
        dlg.CancelButton = dismiss;

        var result = dlg.ShowDialog(this);
        if (result == DialogResult.OK)
        {
            alwaysTray = remember.Checked;
            return CloseChoice.Tray;
        }
        if (result == DialogResult.Abort)
            return CloseChoice.Exit;
        return CloseChoice.Cancel;
    }

    private void SetAlwaysMinimizeToTray(bool value)
    {
        _alwaysMinimizeToTray = value;
        if (_alwaysTrayMenuItem != null && _alwaysTrayMenuItem.Checked != value)
            _alwaysTrayMenuItem.Checked = value;
        SaveAlwaysMinimizeToTray(value);
    }

    private bool BusyElsewhereCopy(out string? detail)
    {
        detail = null;
        var host = _hostBox.Text.Trim();
        if (string.IsNullOrEmpty(host) ||
            !_onlineServers.TryGetValue(host, out var server) ||
            !server.IsBusyElsewhere)
            return false;
        var who = string.IsNullOrWhiteSpace(server.BusyPc) ? "another PC" : server.BusyPc;
        detail = $"Already attached on {who}";
        return true;
    }

    private async Task ReconnectHotkeyAsync()
    {
        Log("Hotkey: reconnect");
        await FindPhoneAsync(autoRefresh: true, quiet: true);
    }

    private static bool LoadStartWithWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("UsbNetBridge") is string v && v.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveStartWithWindows(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")
            ?? throw new InvalidOperationException("Could not open the startup registry key.");
        if (on)
            key.SetValue("UsbNetBridge", "\"" + Application.ExecutablePath + "\" --tray");
        else
            key.DeleteValue("UsbNetBridge", false);
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        _tray.Visible = true;
        if (!_trayTipShown)
        {
            _trayTipShown = true;
            _tray.BalloonTipText = "Still running in the tray. Right-click for Disconnect all or Exit.";
            _tray.ShowBalloonTip(4500);
        }
        Log("Hidden to tray.");
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        SetForegroundWindow(Handle);
        _ = RefreshPortBoxAsync();
    }

    private async Task RefreshPortBoxAsync()
    {
        await ApplyPortStatusAsync(updateStatusWhenAttached: true);
    }

    private async Task ExitFromTrayAsync()
    {
        if (_forceExit)
            return;
        _exiting = true;
        try
        {
            var attached = false;
            try { attached = _cli != null && await _cli.HasAttachedPortsAsync(); }
            catch { /* ignore */ }

            if (attached)
            {
                var answer = MessageBox.Show(
                    "A USB device is still connected on this PC.\n\nDisconnect all before exiting?",
                    "Exit UsbNetBridge",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return;
                if (answer == DialogResult.Yes)
                {
                    try
                    {
                        if (_cli != null) await _cli.DetachAllAsync();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Disconnect all failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
            }

            _tray.Visible = false;
            _forceExit = true;
            Close();
        }
        finally
        {
            if (!_forceExit)
                _exiting = false;
        }
    }

    private static string HostSettingsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UsbNetBridge", "last-host.txt");

    private static string SavedHostsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UsbNetBridge", "saved-hosts.txt");

    private static string AutoConnectPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UsbNetBridge", "auto-connect.txt");

    private static string CloseActionPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UsbNetBridge", "close-action.txt");

    private static bool LoadAlwaysMinimizeToTray()
    {
        try
        {
            var path = CloseActionPath();
            if (!File.Exists(path))
                return false;
            var text = File.ReadAllText(path).Trim();
            return text.Equals("tray", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void SaveAlwaysMinimizeToTray(bool alwaysTray)
    {
        try
        {
            var path = CloseActionPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (alwaysTray)
                File.WriteAllText(path, "tray" + Environment.NewLine);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Preference is optional.
        }
    }

    private sealed record AutoConnectPref(string? Vid, string? Pid, string? BusId, string Label);

    private void LoadSavedHost()
    {
        try
        {
            var path = HostSettingsPath();
            if (File.Exists(path))
            {
                var host = File.ReadAllText(path).Trim();
                if (!string.IsNullOrEmpty(host))
                    _hostBox.Text = host;
            }
        }
        catch { /* ignore */ }

        LoadSavedHosts();
    }

    private static void SaveHost(string host)
    {
        try
        {
            var path = HostSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, host);
        }
        catch { /* ignore */ }
    }

    private void LoadSavedHosts()
    {
        _savedHosts.Clear();
        try
        {
            var path = SavedHostsPath();
            if (File.Exists(path))
            {
                foreach (var line in File.ReadAllLines(path))
                    RememberSavedHost(line, persist: false);
            }
        }
        catch { /* ignore */ }

        var last = _hostBox.Text.Trim();
        if (!string.IsNullOrEmpty(last))
            RememberSavedHost(last, persist: false);

        RefreshSavedHostsUi();
    }

    private void SaveTypedHost()
    {
        var host = _hostBox.Text.Trim();
        if (!RememberSavedHost(host))
        {
            Log("Enter a valid IP to save.");
            return;
        }
        Log($"Saved {host}.");
        RefreshSavedHostsUi(select: host);
    }

    private void RemoveSelectedSavedHost()
    {
        if (_savedHostsBox.SelectedItem is not string host) return;
        _savedHosts.RemoveAll(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase));
        PersistSavedHosts();
        RefreshSavedHostsUi();
        Log($"Removed saved address {host}.");
    }

    private bool RememberSavedHost(string? host, bool persist = true)
    {
        host = host?.Trim() ?? "";
        if (!IsSavableHost(host)) return false;
        var alreadyFirst = _savedHosts.Count > 0 &&
            string.Equals(_savedHosts[0], host, StringComparison.OrdinalIgnoreCase);
        _savedHosts.RemoveAll(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase));
        _savedHosts.Insert(0, host);
        const int maxSaved = 20;
        if (_savedHosts.Count > maxSaved)
            _savedHosts.RemoveRange(maxSaved, _savedHosts.Count - maxSaved);
        if (persist)
        {
            if (!alreadyFirst)
                PersistSavedHosts();
            RefreshSavedHostsUi(select: host);
        }
        return true;
    }

    private static bool IsSavableHost(string host)
    {
        if (!IPAddress.TryParse(host, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false;
        return !IPAddress.IsLoopback(ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.Broadcast);
    }

    private void PersistSavedHosts()
    {
        try
        {
            var path = SavedHostsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, _savedHosts);
        }
        catch { /* ignore */ }
    }

    private void RefreshSavedHostsUi(string? select = null)
    {
        _updatingSavedHostsUi = true;
        try
        {
            var keep = select ?? _savedHostsBox.SelectedItem as string;
            _savedHostsBox.Items.Clear();
            foreach (var h in _savedHosts)
                _savedHostsBox.Items.Add(h);
            if (keep != null)
            {
                for (var i = 0; i < _savedHostsBox.Items.Count; i++)
                {
                    if (_savedHostsBox.Items[i] is string s &&
                        string.Equals(s, keep, StringComparison.OrdinalIgnoreCase))
                    {
                        _savedHostsBox.SelectedIndex = i;
                        break;
                    }
                }
            }
            _removeHostBtn.Enabled = _savedHostsBox.SelectedIndex >= 0;
        }
        finally
        {
            _updatingSavedHostsUi = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopPhoneWatch();
            if (_portPollTimer != null)
            {
                _portPollTimer.Stop();
                _portPollTimer.Dispose();
                _portPollTimer = null;
            }
            if (_inventoryTimer != null)
            {
                _inventoryTimer.Stop();
                _inventoryTimer.Dispose();
                _inventoryTimer = null;
            }
            if (_searchPulseTimer != null)
            {
                _searchPulseTimer.Stop();
                _searchPulseTimer.Dispose();
                _searchPulseTimer = null;
            }
            if (_pingTimer != null)
            {
                _pingTimer.Stop();
                _pingTimer.Dispose();
                _pingTimer = null;
            }
            _phoneEvents?.Dispose();
            _phoneEvents = null;
            _findLock.Dispose();
            try
            {
                UnregisterHotKey(Handle, HotkeyDetachAll);
                UnregisterHotKey(Handle, HotkeyReconnect);
            }
            catch { /* ignore */ }
            _tray.Visible = false;
            _tray.Dispose();
            _circuitBackground?.Dispose();
            _circuitBackground = null;
            InvalidateWindowBackgroundCache();
        }
        base.Dispose(disposing);
    }

    private void Log(string message)
    {
        void Append()
        {
            _logBox.Append(DateTime.Now.ToString("HH:mm:ss"), message);
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "UsbNetBridge");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "activity.log");
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch { /* ignore */ }
        }

        if (InvokeRequired) BeginInvoke(Append);
        else Append();
    }

    /// <summary>Double-buffered title logo rendering sleek vector brand icon + 2-tone title ("UsbNet" cyan + "Bridge" white).</summary>
    private sealed class BufferedLogo : Control
    {
        public BufferedLogo()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0)
                return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Brand font: bold Segoe UI / sans-serif at 25pt
            using var font = UiTheme.TryFont(
                ("Segoe UI", 25f, FontStyle.Bold),
                ("Arial", 25f, FontStyle.Bold),
                ("sans-serif", 25f, FontStyle.Bold));

            const string text1 = "UsbNet";
            const string text2 = "Bridge";
            var sf = StringFormat.GenericTypographic;

            var size1 = g.MeasureString(text1, font, PointF.Empty, sf);
            var size2 = g.MeasureString(text2, font, PointF.Empty, sf);

            float iconSize = Math.Min(48f, Math.Max(36f, Height - 12f));
            float iconMargin = 14f;
            float totalWidth = iconSize + iconMargin + size1.Width + size2.Width;

            float startX = Math.Max(0, (Width - totalWidth) / 2f);
            float iconY = (Height - iconSize) / 2f;
            float textY = (Height - size1.Height) / 2f;

            // Draw vector brand icon
            DrawBrandVectorIcon(g, new RectangleF(startX, iconY, iconSize, iconSize));

            // Draw subtle neon glow behind UsbNet
            float textX1 = startX + iconSize + iconMargin;
            using (var glowBrush = new SolidBrush(Color.FromArgb(35, 0, 240, 255)))
            {
                g.DrawString(text1, font, glowBrush, textX1 - 1f, textY, sf);
                g.DrawString(text1, font, glowBrush, textX1 + 1f, textY, sf);
                g.DrawString(text1, font, glowBrush, textX1, textY - 1f, sf);
                g.DrawString(text1, font, glowBrush, textX1, textY + 1f, sf);
            }

            // Draw 2-Tone Title: "UsbNet" (cyan) + "Bridge" (white)
            using (var brushCyan = new SolidBrush(Color.FromArgb(0, 240, 255)))
            {
                g.DrawString(text1, font, brushCyan, textX1, textY, sf);
            }

            float textX2 = textX1 + size1.Width;
            using (var brushWhite = new SolidBrush(Color.FromArgb(255, 255, 255)))
            {
                g.DrawString(text2, font, brushWhite, textX2, textY, sf);
            }
        }

        private static void DrawBrandVectorIcon(Graphics g, RectangleF bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            var state = g.Save();
            try
            {
                g.TranslateTransform(bounds.X, bounds.Y);
                var scale = Math.Min(bounds.Width / 52f, bounds.Height / 52f);
                g.ScaleTransform(scale, scale);

                // USB Metal Shroud (Top)
                using (var shroudPath = new GraphicsPath())
                {
                    shroudPath.AddLine(12f, 4.5f, 24f, 4.5f);
                    shroudPath.AddArc(23.5f, 4.5f, 2.3f, 2.3f, 270, 90);
                    shroudPath.AddLine(25.8f, 6.3f, 25.8f, 12.5f);
                    shroudPath.AddLine(25.8f, 12.5f, 10.2f, 12.5f);
                    shroudPath.AddLine(10.2f, 12.5f, 10.2f, 6.3f);
                    shroudPath.AddArc(10.2f, 4.5f, 2.3f, 2.3f, 180, 90);
                    shroudPath.CloseFigure();

                    using var fillBrush = new SolidBrush(Color.FromArgb(34, 0, 229, 255));
                    g.FillPath(fillBrush, shroudPath);
                    using var strokePen = new Pen(Color.FromArgb(0, 229, 255), 2.2f)
                    {
                        LineJoin = LineJoin.Round
                    };
                    g.DrawPath(strokePen, shroudPath);
                }

                // USB Contact Pins / Holes inside Shroud
                using (var pinBrush = new SolidBrush(Color.FromArgb(0, 229, 255)))
                {
                    g.FillRectangle(pinBrush, 13f, 6.8f, 3f, 3.4f);
                    g.FillRectangle(pinBrush, 20f, 6.8f, 3f, 3.4f);
                }

                // USB Body Collar (rounded rect)
                using (var bodyPath = new GraphicsPath())
                {
                    float bx = 6f, by = 12.5f, bw = 24f, bh = 21f, br = 3.5f;
                    bodyPath.AddArc(bx + bw - 2 * br, by, 2 * br, 2 * br, 270, 90);
                    bodyPath.AddArc(bx + bw - 2 * br, by + bh - 2 * br, 2 * br, 2 * br, 0, 90);
                    bodyPath.AddArc(bx, by + bh - 2 * br, 2 * br, 2 * br, 90, 90);
                    bodyPath.AddArc(bx, by, 2 * br, 2 * br, 180, 90);
                    bodyPath.CloseFigure();

                    using var bodyFill = new SolidBrush(Color.FromArgb(40, 11, 19, 38));
                    g.FillPath(bodyFill, bodyPath);
                    using var bodyPen = new Pen(Color.FromArgb(0, 229, 255), 2.2f)
                    {
                        LineJoin = LineJoin.Round
                    };
                    g.DrawPath(bodyPen, bodyPath);
                }

                // USB Internal Trident Arrow Stem
                using (var stemPen = new Pen(Color.FromArgb(0, 229, 255), 2.0f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(stemPen, 18f, 30.5f, 18f, 18.5f);
                }

                // Trident Arrowhead (pointing up)
                using (var arrowBrush = new SolidBrush(Color.FromArgb(0, 229, 255)))
                {
                    PointF[] arrow = { new(18f, 14.5f), new(14.5f, 19f), new(21.5f, 19f) };
                    g.FillPolygon(arrowBrush, arrow);
                }

                // Trident Left Branch (Curved to Circle)
                using (var branchPen = new Pen(Color.FromArgb(0, 229, 255), 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    using var p = new GraphicsPath();
                    p.AddBezier(18f, 25.5f, 15.5f, 25.5f, 12.5f, 24.5f, 12.5f, 22f);
                    p.AddLine(12.5f, 22f, 12.5f, 20.8f);
                    g.DrawPath(branchPen, p);
                }
                using (var circleBrush = new SolidBrush(Color.FromArgb(0, 229, 255)))
                {
                    g.FillEllipse(circleBrush, 12.5f - 1.6f, 19.5f - 1.6f, 3.2f, 3.2f);
                }

                // Trident Right Branch (Curved to Square)
                using (var branchPen = new Pen(Color.FromArgb(0, 229, 255), 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    using var p = new GraphicsPath();
                    p.AddBezier(18f, 24.5f, 20.5f, 24.5f, 23.5f, 23.5f, 23.5f, 21.5f);
                    p.AddLine(23.5f, 21.5f, 23.5f, 20.8f);
                    g.DrawPath(branchPen, p);
                }
                using (var squareBrush = new SolidBrush(Color.FromArgb(0, 229, 255)))
                {
                    g.FillRectangle(squareBrush, 22.2f, 18.8f, 2.6f, 2.6f);
                }

                // Cable Loop from Bottom of Plug
                using (var cablePen = new Pen(Color.FromArgb(0, 180, 216), 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    using var p = new GraphicsPath();
                    p.AddBezier(18f, 33.5f, 18f, 40f, 19.5f, 45f, 25f, 45f);
                    p.AddBezier(25f, 45f, 30.5f, 45f, 32.5f, 40.5f, 32.5f, 35f);
                    p.AddLine(32.5f, 35f, 32.5f, 30f);
                    g.DrawPath(cablePen, p);
                }

                // Circuit Node at Cable End
                using (var nodeBrush = new SolidBrush(Color.FromArgb(0, 229, 255)))
                {
                    g.FillEllipse(nodeBrush, 32.5f - 2.2f, 28.5f - 2.2f, 4.4f, 4.4f);
                }

                // Wi-Fi Broadcast Waves (Upper Right of Plug)
                // Wave 1
                using (var wave1Pen = new Pen(Color.FromArgb(0, 180, 216), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    using var p = new GraphicsPath();
                    p.AddBezier(30f, 12.5f, 32.5f, 9.8f, 35.8f, 8.2f, 39.5f, 8.2f);
                    g.DrawPath(wave1Pen, p);
                }
                // Wave 2
                using (var wave2Pen = new Pen(Color.FromArgb(0, 210, 255), 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    using var p = new GraphicsPath();
                    p.AddBezier(33f, 16f, 36.8f, 11.5f, 41.5f, 9f, 46.5f, 9f);
                    g.DrawPath(wave2Pen, p);
                }
                // Wave 3
                using (var wave3Pen = new Pen(Color.FromArgb(0, 240, 255), 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    using var p = new GraphicsPath();
                    p.AddBezier(36f, 19.5f, 41f, 13f, 46f, 10f, 51f, 10f);
                    g.DrawPath(wave3Pen, p);
                }
            }
            finally
            {
                g.Restore(state);
            }
        }
    }

    /// <summary>Centered tagline with soft cyan type and side flourishes.</summary>
    private sealed class HeroTagline : Control
    {
        private string _tagline = "";

        public HeroTagline()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public string Tagline
        {
            get => _tagline;
            set
            {
                _tagline = value ?? "";
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (string.IsNullOrWhiteSpace(_tagline))
                return;

            using var font = TryFont(
                ("Segoe UI Semilight", 10.5f, FontStyle.Italic),
                ("Segoe UI Light", 10.5f, FontStyle.Italic),
                ("Segoe UI", 10.25f, FontStyle.Italic));

            var ink = Color.FromArgb(190, 220, 240);
            using var brush = new SolidBrush(ink);
            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter,
            };

            var textSize = g.MeasureString(_tagline, font, Width, format);
            var cx = Width / 2f;
            var cy = Height / 2f;
            var textLeft = cx - textSize.Width / 2f;
            var textRight = cx + textSize.Width / 2f;

            // Soft side rules — thin cyan lines with a small diamond accent.
            var ruleY = cy;
            var gap = 14f;
            var ruleColor = Color.FromArgb(90, Accent.R, Accent.G, Accent.B);
            using var pen = new Pen(ruleColor, 1f);
            var leftEnd = Math.Max(8f, textLeft - gap);
            var rightStart = Math.Min(Width - 8f, textRight + gap);
            if (leftEnd > 24f)
            {
                g.DrawLine(pen, 12f, ruleY, leftEnd - 6f, ruleY);
                DrawDiamond(g, leftEnd - 2f, ruleY, 3.2f, ink);
            }
            if (rightStart < Width - 24f)
            {
                DrawDiamond(g, rightStart + 2f, ruleY, 3.2f, ink);
                g.DrawLine(pen, rightStart + 6f, ruleY, Width - 12f, ruleY);
            }

            var rect = ClientRectangle;
            g.DrawString(_tagline, font, brush, rect, format);
        }

        private static void DrawDiamond(Graphics g, float x, float y, float r, Color color)
        {
            using var b = new SolidBrush(Color.FromArgb(160, color));
            var pts = new[]
            {
                new PointF(x, y - r),
                new PointF(x + r, y),
                new PointF(x, y + r),
                new PointF(x - r, y),
            };
            g.FillPolygon(b, pts);
        }

        private static Font TryFont(params (string Family, float Size, FontStyle Style)[] options)
        {
            foreach (var (family, size, style) in options)
            {
                try
                {
                    var f = new Font(family, size, style, GraphicsUnit.Point);
                    if (string.Equals(f.FontFamily.Name, family, StringComparison.OrdinalIgnoreCase) ||
                        family.StartsWith("Segoe UI", StringComparison.OrdinalIgnoreCase))
                        return f;
                    f.Dispose();
                }
                catch
                {
                    // try next
                }
            }
            return new Font("Segoe UI", 10.25f, FontStyle.Italic, GraphicsUnit.Point);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/appzbynate/UsbNetBridge/releases/latest");
            req.Headers.UserAgent.ParseAdd("UsbNetBridge-Client/1.0");
            using var res = await _httpClient.SendAsync(req);
            if (!res.IsSuccessStatusCode) return;
            var json = await res.Content.ReadAsStringAsync();
            var tagMatch = System.Text.RegularExpressions.Regex.Match(json, @"""tag_name"":\s*""v?([0-9]+\.[0-9]+\.[0-9]+)""");
            if (!tagMatch.Success) return;
            var latestVersionStr = tagMatch.Groups[1].Value;
            if (Version.TryParse(latestVersionStr, out var latestVersion))
            {
                var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (currentVersion != null && latestVersion > currentVersion)
                {
                    Invoke(() =>
                    {
                        if (_updateBanner != null)
                            _updateBanner.Visible = true;
                    });
                }
            }
        }
        catch (Exception) { /* ignore network errors */ }
    }
}
