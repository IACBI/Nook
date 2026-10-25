using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Nook;

internal sealed class MainForm : Form
{
    private static readonly Color AccentColor = Color.FromArgb(0, 102, 204);

    // Only NVIDIA and AMD publish a temperature and clock API we can call. Intel's
    // integrated adapters report neither, so say why instead of just "Unavailable".
    private const string NoDriverSensorText = "No driver sensor";

    // Marks secondary text. ApplyTheme used to detect it by its current colour, which it overwrites itself.
    private const string MutedTag = "Muted";

    private const int WmHotKey = 0x0312;
    private const int HotKeyToggleOverlay = 1;
    private const int HotKeyToggleLock = 2;

    private readonly Button _navProcessBtn = CreateNavButton("Process");
    private readonly Button _navGpuBtn = CreateNavButton("GPU");
    private readonly Button _navSettingsBtn = CreateNavButton("Settings");
    private readonly Panel _contentContainer = new() { Dock = DockStyle.Fill };
    private readonly Panel _processView = new() { Dock = DockStyle.Fill };
    private readonly Panel _gpuView = new() { Dock = DockStyle.Fill };
    private readonly Panel _settingsView = new() { Dock = DockStyle.Fill };

    private readonly ComboBox _processSelector = new();
    private readonly TextBox _searchBox = new();
    private readonly CheckBox _vramFilterCheck = new() { Text = "Only VRAM active" };
    private readonly ComboBox _gpuSelector = new();
    private readonly Button _refreshButton = new();
    private readonly Label _currentValue = CreateValueLabel();
    private readonly Label _peakValue = CreateValueLabel();
    private readonly Label _averageValue = CreateValueLabel();
    private readonly Label _processSharedValue = CreateValueLabel();
    private readonly Label _gpuUsageValue = CreateValueLabel();
    private readonly Label _gpuMemoryValue = CreateValueLabel();
    private readonly Label _gpuSharedValue = CreateValueLabel();
    private readonly Label _gpuTempValue = CreateValueLabel();
    private readonly Label _gpuClockValue = CreateValueLabel();
    private readonly Label _statusLabel = new();
    private readonly CheckBox _overlayVisible = new() { Text = "Overlay visible" };
    private readonly CheckBox _overlayLocked = new() { Text = "Click-through locked" };
    private readonly ContextMenuStrip _cornerMenu = new();
    private readonly ToolTip _toolTip = new() { AutomaticDelay = 250, AutoPopDelay = 5000, InitialDelay = 250, ReshowDelay = 100, ShowAlways = true };
    // Sampling runs on GpuSampler's thread; this timer only watches it, so it keeps ticking when a driver hangs.
    private readonly System.Windows.Forms.Timer _watchdogTimer = new() { Interval = 1000 };
    private static readonly TimeSpan SamplerStallThreshold = TimeSpan.FromSeconds(3);
    private readonly System.Windows.Forms.Timer _processRefreshTimer = new() { Interval = 10000 };
    // Debounced so dragging the overlay or toggling options writes the file once, not per event.
    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 1500 };
    private readonly Label _hotkeyInfo = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Tag = MutedTag, Margin = new Padding(0, 0, 0, 16) };
    private readonly Label _shortcutHint = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Tag = MutedTag, Margin = new Padding(8, 8, 0, 0) };
    private readonly AppSettings _settings = SettingsStore.Load();
    private readonly NotifyIcon _notifyIcon = new();
    private readonly ContextMenuStrip _trayMenu = new();

    private readonly Dictionary<GpuLuid, GpuAdapterItem> _adapterLookup = [];
    private readonly HashSet<int> _activeVramPids = [];

    private ToolStripMenuItem? _trayOverlayItem;
    private ToolStripMenuItem? _trayLockItem;
    private readonly Func<ISampleSource> _createSampleSource;
    private GpuSampler? _sampler;
    private bool _samplerStalled;
    private OverlayForm? _overlay;
    private List<ProcessItem> _allProcesses = [];
    private List<ProcessItem> _filteredProcesses = [];
    private readonly ProcessTracker _processTracker = new();
    private bool _isLoadingProcesses;
    private long _sampleCount;
    private double _sampleTotalBytes;
    private long _peakBytes;
    private bool _isRefreshingProcesses;
    private bool _isRefreshingGpus;
    private bool _adapterCatalogLoaded;
    private bool _hasShownTrayBalloon;
    private string _lastGpuName = "GPU unavailable";
    private string _lastGpuUsage = "—";
    private string _lastGpuMemory = "—";
    private string _lastGpuShared = "—";
    private string _lastGpuTemp = "—";
    private string _lastGpuClock = "—";
    private long _lastGpuDedicatedBytes;
    private string? _lastProcessMemory;
    private string? _lastProcessShared;
    private bool _exitRequested;
    private string? _hotkeyWarning;
    private string? _lastLoggedReadError;

    public MainForm() : this(() => new NativeSampleSource())
    {
    }

    /// <summary>Lets a test drive the window with a sample source that can be made to hang.</summary>
    internal MainForm(Func<ISampleSource> createSampleSource)
    {
        _createSampleSource = createSampleSource;
        Text = "Nook";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 480);
        MinimumSize = new Size(540, 430);
        FormBorderStyle = FormBorderStyle.Sizable;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);

        _cornerMenu.Renderer = new DarkMenuRenderer();
        _trayMenu.Renderer = new DarkMenuRenderer();
        _processTracker.RememberedName = _settings.SelectedProcessName;

        BuildLayout();
        InitializeTrayIcon();
        ApplyTheme();

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _watchdogTimer.Tick += (_, _) => CheckSamplerHealth();
        _processRefreshTimer.Tick += (_, _) => RefreshProcesses();
        _saveTimer.Tick += (_, _) => SaveSettings();
        Shown += OnShown;
        FormClosing += OnFormClosing;
        Resize += OnResize;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterHotKeys();
    }

    private void RegisterHotKeys()
    {
        if (!IsHandleCreated) return;
        UnregisterHotKey(Handle, HotKeyToggleOverlay);
        UnregisterHotKey(Handle, HotKeyToggleLock);

        var overlayKeys = FormatHotkey(_settings.HotkeyToggleOverlayModifiers, _settings.HotkeyToggleOverlayKey);
        var lockKeys = FormatHotkey(_settings.HotkeyToggleLockModifiers, _settings.HotkeyToggleLockKey);
        var overlayOk = RegisterHotKey(Handle, HotKeyToggleOverlay, _settings.HotkeyToggleOverlayModifiers, _settings.HotkeyToggleOverlayKey);
        var lockOk = RegisterHotKey(Handle, HotKeyToggleLock, _settings.HotkeyToggleLockModifiers, _settings.HotkeyToggleLockKey);

        // The handle is recreated whenever ShowInTaskbar changes, so only a change in the outcome is news.
        var warning = overlayOk && lockOk
            ? null
            : $"Shortcut unavailable, another application already uses it: {string.Join(", ", new[] { overlayOk ? null : overlayKeys, lockOk ? null : lockKeys }.OfType<string>())}.";
        if (warning is not null && warning != _hotkeyWarning)
        {
            Log.Warn(warning);
        }

        _hotkeyWarning = warning;
        _hotkeyInfo.Text = $"Toggle Overlay: {overlayKeys}{(overlayOk ? "" : "  (unavailable)")}\nToggle Lock: {lockKeys}{(lockOk ? "" : "  (unavailable)")}";
        _shortcutHint.Text = $"{overlayKeys} / {lockKeys}";
    }

    /// <summary>Formats a RegisterHotKey modifier mask and virtual key the way Windows menus show them.</summary>
    internal static string FormatHotkey(uint modifiers, uint virtualKey)
    {
        var parts = new List<string>(5);
        if ((modifiers & 0x2) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x1) != 0) parts.Add("Alt");
        if ((modifiers & 0x4) != 0) parts.Add("Shift");
        if ((modifiers & 0x8) != 0) parts.Add("Win");
        var key = (Keys)virtualKey;
        parts.Add(key is >= Keys.D0 and <= Keys.D9 ? ((char)virtualKey).ToString() : key.ToString());
        return string.Join("+", parts);
    }

    private void OnShown(object? sender, EventArgs e)
    {
        CreateOverlay();
        BeginInvoke(InitializeMonitoring);
    }

    private void InitializeMonitoring()
    {
        RefreshProcesses();
        LoadAdapterCatalog();
        if (SettingsStore.LastLoadProblem is { } settingsProblem)
        {
            Log.Warn(settingsProblem);
        }

        // A portable exe can be moved; refresh the Run entry so it does not keep starting a stale path.
        if (_settings.StartWithWindows)
        {
            SetStartWithWindows(true);
        }

        _sampler = new GpuSampler(_createSampleSource, PostSnapshot, TimeSpan.FromSeconds(1), Log.Warn);
        UpdateSampleRequest();
        _sampler.Start();
        SetStatus(_hotkeyWarning ?? "Ready. Values update once per second.");
        _watchdogTimer.Start();
        _processRefreshTimer.Start();
    }

    /// <summary>
    /// Enumerates DXGI adapters off the UI thread: it is a driver call like any other, and a hang here
    /// would otherwise freeze the window before the first sample.
    /// </summary>
    private async void LoadAdapterCatalog()
    {
        try
        {
            var adapters = await Task.Run(GpuAdapterCatalog.GetAdapters);
            if (IsDisposed)
            {
                return;
            }

            foreach (var adapter in adapters)
            {
                _adapterLookup[adapter.Luid] = adapter;
            }
        }
        catch (Exception ex)
        {
            Log.Error("DXGI adapter enumeration failed.", ex);
        }

        _adapterCatalogLoaded = true;
        Log.Info(_adapterLookup.Count == 0
            ? "DXGI listed no adapters; names come from the GPU counters."
            : $"Adapters: {string.Join("; ", _adapterLookup.Values.Select(adapter => $"{adapter.Name} ({adapter.Luid})"))}");
        RefreshGpuList(null);
        UpdateSampleRequest();
    }

    /// <summary>Runs on the sampler thread; everything past the hand-off runs on the UI thread.</summary>
    private void PostSnapshot(GpuSnapshot snapshot)
    {
        try
        {
            BeginInvoke(() => ApplySnapshot(snapshot));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The window is closing, or its handle is being recreated; the next sample follows within a second.
        }
    }

    /// <summary>Tells the sampler which GPU and process the UI shows now. Cheap: an unchanged request is not republished.</summary>
    private void UpdateSampleRequest()
    {
        if (_sampler is null)
        {
            return;
        }

        var gpu = _gpuSelector.SelectedItem as GpuAdapterItem;
        var process = _processTracker.Current;
        var request = new SampleRequest(gpu?.Luid, gpu?.Name, process?.Pid, process?.Name);
        if (request != _sampler.Request)
        {
            _sampler.Request = request;
        }
    }

    private void CheckSamplerHealth()
    {
        if (_sampler is null)
        {
            return;
        }

        var busy = _sampler.BusyFor;
        if (busy < SamplerStallThreshold)
        {
            return;
        }

        if (!_samplerStalled)
        {
            _samplerStalled = true;
            Log.Warn($"The GPU driver has not answered for {busy.TotalSeconds:0} s; readings are paused.");
            ClearCurrentValues();
            UpdateOverlay();
        }

        SetStatus($"The GPU driver is not responding ({busy.TotalSeconds:0} s). Readings resume when it answers.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (IsHandleCreated)
            {
                UnregisterHotKey(Handle, HotKeyToggleOverlay);
                UnregisterHotKey(Handle, HotKeyToggleLock);
            }

            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            // First, so no snapshot is posted to a window that is going away. Waits at most two seconds for a hung driver.
            _sampler?.Dispose();
            _watchdogTimer.Dispose();
            _processRefreshTimer.Dispose();
            _saveTimer.Dispose();
            _notifyIcon.Dispose();
            _trayMenu.Dispose();
            _overlay?.Dispose();
            _cornerMenu.Dispose();
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotKey)
        {
            if (m.WParam.ToInt32() == HotKeyToggleOverlay)
            {
                ToggleOverlayVisibility();
                return;
            }

            if (m.WParam.ToInt32() == HotKeyToggleLock)
            {
                ToggleOverlayLock();
                return;
            }
        }

        base.WndProc(ref m);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 16, 18, 14),
            ColumnCount = 1,
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); // Header
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); // Nav Bar
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Content
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // Status Bar
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); // Bottom Controls

        // Header Layout
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label { Text = "Nook", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 15.5f), TextAlign = ContentAlignment.BottomLeft, AccessibleName = "Application title" };
        var subtitle = new Label { Text = "Per-process GPU memory, load, temperature and clock", Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText, Tag = MutedTag, TextAlign = ContentAlignment.TopLeft };
        var cadence = new Label { Text = "WINDOWS-NATIVE  •  1 SECOND", AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Font = new Font("Segoe UI Semibold", 8f), Padding = new Padding(10, 5, 10, 5), Tag = "AccentBadge" };
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(subtitle, 0, 1);
        header.Controls.Add(cadence, 1, 0);
        header.SetRowSpan(cadence, 2);

        // Segmented Nav Bar
        var navBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 4, 0, 4)
        };

        _navProcessBtn.Click += (_, _) => SwitchView("Process");
        _navGpuBtn.Click += (_, _) => SwitchView("GPU");
        _navSettingsBtn.Click += (_, _) => SwitchView("Settings");
        navBar.Controls.AddRange([_navProcessBtn, _navGpuBtn, _navSettingsBtn]);

        // Views
        BuildProcessView(_processView);
        BuildGpuView(_gpuView);
        BuildSettingsView(_settingsView);

        _contentContainer.Controls.Add(_processView);
        _contentContainer.Controls.Add(_gpuView);
        _contentContainer.Controls.Add(_settingsView);
        SwitchView("Process");

        // Status Label
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = SystemColors.GrayText;
        _statusLabel.Tag = MutedTag;
        _statusLabel.AutoEllipsis = true;
        _statusLabel.AccessibleName = "Monitoring status";

        // Bottom Commands Bar
        _overlayVisible.AutoSize = true;
        _overlayVisible.Margin = new Padding(0, 7, 14, 0);
        _overlayVisible.AccessibleName = "Show overlay";
        _overlayVisible.CheckedChanged += (_, _) => SetOverlayVisibility(_overlayVisible.Checked);

        _overlayLocked.AutoSize = true;
        _overlayLocked.Margin = new Padding(0, 7, 14, 0);
        _overlayLocked.AccessibleName = "Lock overlay click-through mode";
        _overlayLocked.CheckedChanged += (_, _) => SetOverlayLock(_overlayLocked.Checked);

        var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty, AccessibleName = "Overlay controls" };
        var corners = CreateActionButton("Position", "Choose overlay corner position");
        corners.Click += (_, _) => ShowCornerMenu(corners);
        AddCornerMenuItem("Top left", OverlayCorner.TopLeft);
        AddCornerMenuItem("Top right", OverlayCorner.TopRight);
        AddCornerMenuItem("Bottom left", OverlayCorner.BottomLeft);
        AddCornerMenuItem("Bottom right", OverlayCorner.BottomRight);

        commands.Controls.AddRange([_overlayVisible, _overlayLocked, corners, _shortcutHint]);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(navBar, 0, 1);
        root.Controls.Add(_contentContainer, 0, 2);
        root.Controls.Add(_statusLabel, 0, 3);
        root.Controls.Add(commands, 0, 4);
        Controls.Add(root);
    }

    private static Button CreateNavButton(string text) => new()
    {
        Text = text,
        Size = new Size(100, 28),
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(0, 0, 6, 0),
        Font = new Font("Segoe UI Semibold", 9f),
        Tag = "NavButton"
    };

    private void SwitchView(string viewName)
    {
        _processView.Visible = viewName == "Process";
        _gpuView.Visible = viewName == "GPU";
        _settingsView.Visible = viewName == "Settings";

        UpdateNavButtonStyle(_navProcessBtn, viewName == "Process");
        UpdateNavButtonStyle(_navGpuBtn, viewName == "GPU");
        UpdateNavButtonStyle(_navSettingsBtn, viewName == "Settings");
    }

    private void UpdateNavButtonStyle(Button btn, bool isActive)
    {
        var dark = IsDarkModeEnabled();
        if (isActive)
        {
            btn.BackColor = AccentColor;
            btn.ForeColor = Color.White;
            btn.FlatAppearance.BorderSize = 0;
        }
        else
        {
            btn.BackColor = dark ? Color.FromArgb(36, 42, 52) : Color.FromArgb(232, 236, 242);
            btn.ForeColor = dark ? Color.FromArgb(220, 228, 240) : Color.FromArgb(30, 40, 55);
            btn.FlatAppearance.BorderSize = 0;
        }
    }

    private void BuildProcessView(Panel container)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); // Filter Row
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); // Selection Row
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); // Source Label Row
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Metrics Panel Row
        
        // Filter / Search Row
        var filterRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        var searchLabel = new Label { Text = "Search", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9f) };
        _searchBox.Dock = DockStyle.Fill;
        _searchBox.Margin = new Padding(0, 3, 8, 3);
        _searchBox.PlaceholderText = "Type process name...";
        _searchBox.TextChanged += (_, _) => ApplyProcessFilters();

        _vramFilterCheck.Dock = DockStyle.Fill;
        _vramFilterCheck.Checked = _settings.OnlyShowVramProcesses;
        _vramFilterCheck.CheckedChanged += (_, _) =>
        {
            _settings.OnlyShowVramProcesses = _vramFilterCheck.Checked;
            RequestSave();
            ApplyProcessFilters();
        };

        filterRow.Controls.Add(searchLabel, 0, 0);
        filterRow.Controls.Add(_searchBox, 1, 0);
        filterRow.Controls.Add(_vramFilterCheck, 2, 0);

        // Selection Row
        var selection = CreateSelectionRow("Process", _processSelector, _refreshButton);
        _processSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _processSelector.AccessibleName = "Process to monitor";
        _processSelector.SelectedIndexChanged += (_, _) => SelectProcess();

        _refreshButton.Text = "Refresh";
        StyleActionButton(_refreshButton, "Refresh running process list");
        _refreshButton.Click += (_, _) => RefreshProcesses();

        var source = CreateSourceLabel("Windows-reported GPU memory for the selected process");
        var metrics = CreateMetricsPanel(new[]
        {
            ("Dedicated (current)", _currentValue),
            ("Dedicated (peak)", _peakValue),
            ("Dedicated (average)", _averageValue),
            ("Shared (current)", _processSharedValue)
        });

        layout.Controls.Add(filterRow, 0, 0);
        layout.Controls.Add(selection, 0, 1);
        layout.Controls.Add(source, 0, 2);
        layout.Controls.Add(metrics, 0, 3);
        container.Controls.Add(layout);
    }

    private void BuildGpuView(Panel container)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); // Selection Row
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); // Source Label Row
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Metrics Panel Row

        var selection = CreateSelectionRow("GPU", _gpuSelector, null);
        _gpuSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _gpuSelector.AccessibleName = "GPU adapter to monitor";
        _gpuSelector.SelectedIndexChanged += (_, _) => SelectGpu();

        var source = CreateSourceLabel("Windows adapter & driver telemetry");
        var metrics = CreateMetricsPanel(new[] {
            ("Usage (engine)", _gpuUsageValue),
            ("Dedicated VRAM", _gpuMemoryValue),
            ("Shared memory", _gpuSharedValue),
            ("GPU Temperature", _gpuTempValue),
            ("Core Clock Speed", _gpuClockValue)
        });

        layout.Controls.Add(selection, 0, 0);
        layout.Controls.Add(source, 0, 1);
        layout.Controls.Add(metrics, 0, 2);
        container.Controls.Add(layout);
    }

    private void BuildSettingsView(Panel container)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(12, 10, 12, 10)
        };

        var trayHeader = new Label { Text = "System Tray and Startup", Font = new Font("Segoe UI Semibold", 10f), AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        var minimizeCheck = new CheckBox { Text = "Minimize window to system tray", AutoSize = true, Checked = _settings.MinimizeToTray, Margin = new Padding(0, 0, 0, 6) };
        minimizeCheck.CheckedChanged += (_, _) =>
        {
            _settings.MinimizeToTray = minimizeCheck.Checked;
            RequestSave();
        };

        var closeCheck = new CheckBox { Text = "Close button minimizes to system tray", AutoSize = true, Checked = _settings.CloseToTray, Margin = new Padding(0, 0, 0, 6) };
        closeCheck.CheckedChanged += (_, _) =>
        {
            _settings.CloseToTray = closeCheck.Checked;
            RequestSave();
        };

        var startupCheck = new CheckBox { Text = "Start automatically with Windows", AutoSize = true, Checked = _settings.StartWithWindows, Margin = new Padding(0, 0, 0, 16) };
        startupCheck.CheckedChanged += (_, _) =>
        {
            _settings.StartWithWindows = startupCheck.Checked;
            SetStartWithWindows(startupCheck.Checked);
            RequestSave();
        };

        var hotkeyHeader = new Label { Text = "Global Hotkeys", Font = new Font("Segoe UI Semibold", 10f), AutoSize = true, Margin = new Padding(0, 0, 0, 8) };

        panel.Controls.AddRange([trayHeader, minimizeCheck, closeCheck, startupCheck, hotkeyHeader, _hotkeyInfo]);
        container.Controls.Add(panel);
    }

    private void InitializeTrayIcon()
    {
        var appIcon = AppIconGenerator.GetAppIcon();
        Icon = appIcon;
        _notifyIcon.Icon = appIcon;

        _notifyIcon.Text = "Nook — GPU monitor";
        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();

        _trayOverlayItem = new ToolStripMenuItem("Overlay Visible", null, (_, _) => ToggleOverlayVisibility()) { Checked = _settings.OverlayVisible };
        _trayLockItem = new ToolStripMenuItem("Lock Overlay", null, (_, _) => ToggleOverlayLock()) { Checked = _settings.OverlayLocked };

        _trayMenu.Items.Add("Show / Hide Main Window", null, (_, _) => ToggleWindowVisibility());
        _trayMenu.Items.Add("-");
        _trayMenu.Items.Add(_trayOverlayItem);
        _trayMenu.Items.Add(_trayLockItem);
        _trayMenu.Items.Add("-");
        _trayMenu.Items.Add("Exit", null, (_, _) =>
        {
            _exitRequested = true;
            Close();
        });

        _notifyIcon.ContextMenuStrip = _trayMenu;
    }

    private void ToggleWindowVisibility()
    {
        if (Visible && WindowState != FormWindowState.Minimized)
        {
            Hide();
        }
        else
        {
            RestoreFromTray();
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        BringToFront();
        Activate();
    }

    private void OnResize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized && _settings.MinimizeToTray)
        {
            ShowInTaskbar = false;
            Hide();
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_settings.CloseToTray && !_exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            ShowInTaskbar = false;
            Hide();

            if (!_hasShownTrayBalloon)
            {
                _hasShownTrayBalloon = true;
                _notifyIcon.ShowBalloonTip(2000, "Nook", "Application is running in the system tray.", ToolTipIcon.Info);
            }
        }
        else
        {
            _saveTimer.Stop();
            SaveSettings();
        }
    }

    private static TableLayoutPanel CreateSelectionRow(string labelText, ComboBox selector, Button? action)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = action is null ? 2 : 3, RowCount = 1, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (action is not null)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        }

        var label = new Label { Text = labelText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9f) };
        selector.Dock = DockStyle.Fill;
        selector.Margin = new Padding(0, 3, action is null ? 0 : 8, 3);
        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(selector, 1, 0);
        if (action is not null)
        {
            action.Dock = DockStyle.Fill;
            action.Margin = new Padding(0, 2, 0, 2);
            layout.Controls.Add(action, 2, 0);
        }

        return layout;
    }

    private static Label CreateSourceLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        ForeColor = AccentColor,
        Font = new Font("Segoe UI Semibold", 8.5f),
        TextAlign = ContentAlignment.MiddleLeft,
        UseMnemonic = false, // otherwise "&" is swallowed as an access-key prefix
        Tag = "AccentText"
    };

    private Button CreateActionButton(string text, string toolTip)
    {
        var button = new Button { Text = text, AutoSize = false, Size = new Size(94, 29) };
        StyleActionButton(button, toolTip);
        return button;
    }

    private void StyleActionButton(Button button, string toolTip)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.Tag = "ActionButton";
        button.AccessibleName = button.Text;
        button.MouseEnter += (_, _) => button.BackColor = AccentColor;
        button.MouseLeave += (_, _) => ApplyButtonBaseColor(button);
        button.MouseDown += (_, _) => button.BackColor = Color.FromArgb(0, 78, 150);
        button.MouseUp += (_, _) => button.BackColor = AccentColor;
        _toolTip.SetToolTip(button, toolTip);
    }

    private void ApplyButtonBaseColor(Button button)
    {
        button.BackColor = IsDarkModeEnabled() ? Color.FromArgb(43, 50, 62) : Color.White;
        button.ForeColor = IsDarkModeEnabled() ? Color.FromArgb(239, 244, 251) : Color.FromArgb(23, 34, 49);
        button.FlatAppearance.BorderColor = IsDarkModeEnabled() ? Color.FromArgb(75, 87, 105) : Color.FromArgb(196, 207, 222);
    }

    private static TableLayoutPanel CreateMetricsPanel((string Name, Label Value)[] metrics)
    {
        var rowCount = metrics.Length;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rowCount, Margin = new Padding(0, 4, 0, 0) };
        for (var row = 0; row < rowCount; row++)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rowCount));
            var card = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, row == 0 ? 0 : 6, 0, 0), Padding = new Padding(14, 0, 14, 0), Tag = "MetricCard" };
            var label = new Label { Text = metrics[row].Name, Dock = DockStyle.Left, Width = 230, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9f), AccessibleName = $"{metrics[row].Name} label" };
            metrics[row].Value.AccessibleName = metrics[row].Name;
            card.Controls.Add(metrics[row].Value);
            card.Controls.Add(label);
            panel.Controls.Add(card, 0, row);
        }

        return panel;
    }

    private static Label CreateValueLabel() => new()
    {
        Dock = DockStyle.Fill,
        Text = "—",
        TextAlign = ContentAlignment.MiddleRight,
        Font = new Font("Segoe UI Semibold", 15f)
    };

    private void ApplySnapshot(GpuSnapshot snapshot)
    {
        if (IsDisposed)
        {
            return;
        }

        if (_samplerStalled)
        {
            _samplerStalled = false;
            Log.Info("The GPU driver is answering again.");
            // Otherwise the stall message stays up until something else happens to replace it.
            SetStatus(_hotkeyWarning ?? "Ready. Values update once per second.");
        }

        try
        {
            // The snapshot answers for the PID it was asked about; right after a new pick it may still describe the old one.
            var monitored = _processTracker.Current;
            if (monitored is not null && snapshot.Request.Pid == monitored.Pid && snapshot.Process.Checked &&
                _processTracker.CheckAlive(_ => snapshot.Process.Live) == TrackerEvent.Exited)
            {
                ResetStatistics();
                SetStatus($"{monitored.Name} exited. Nook will follow it when it starts again.");
            }

            if (!snapshot.IsAvailable)
            {
                var error = snapshot.Error ?? "Windows GPU counters are unavailable.";
                LogReadError(error, snapshot.ErrorException);
                SetStatus(error);
                ClearCurrentValues();
                UpdateOverlay();
                return;
            }

            if (_lastLoggedReadError is not null)
            {
                Log.Info("Windows GPU counters recovered.");
                _lastLoggedReadError = null;
            }

            var processSamples = snapshot.ProcessDedicatedBytes;
            var processShared = snapshot.ProcessSharedBytes;
            _activeVramPids.Clear();
            _activeVramPids.UnionWith(processSamples.Keys);
            _activeVramPids.UnionWith(processShared.Keys);
            RefreshGpuList(snapshot);
            SampleSelectedProcess(processSamples, processShared);
            SampleSelectedGpu(snapshot);
            if (_processTracker.Current is { } tracked && processSamples.ContainsKey(tracked.Pid))
            {
                SetStatus($"Monitoring {tracked.Name} (PID {tracked.Pid}).");
            }
            UpdateOverlay();
        }
        catch (Exception ex)
        {
            LogReadError($"A GPU sample could not be shown: {ex.Message}", ex);
            ClearCurrentValues();
            SetStatus($"A GPU sample could not be shown: {ex.Message}");
            UpdateOverlay();
        }
        finally
        {
            UpdateSampleRequest();
        }
    }

    /// <summary>Logs a sampling failure once per distinct message; the sampler would otherwise repeat it every second.</summary>
    private void LogReadError(string error, Exception? exception)
    {
        if (error == _lastLoggedReadError)
        {
            return;
        }

        _lastLoggedReadError = error;
        if (exception is null)
        {
            Log.Warn(error);
        }
        else
        {
            Log.Error(error, exception);
        }
    }

    private void SampleSelectedProcess(IReadOnlyDictionary<int, long> dedicated, IReadOnlyDictionary<int, long> shared)
    {
        _lastProcessMemory = null;
        _lastProcessShared = null;
        if (_processTracker.Current is not { } monitored)
        {
            return;
        }

        var hasDedicated = dedicated.TryGetValue(monitored.Pid, out var dedicatedBytes);
        var hasShared = shared.TryGetValue(monitored.Pid, out var sharedBytes);
        if (!hasDedicated && !hasShared)
        {
            _currentValue.Text = "—";
            _processSharedValue.Text = "—";
            SetStatus("Windows has no GPU memory sample for this process.");
            return;
        }

        _sampleCount++;
        _sampleTotalBytes += dedicatedBytes;
        _peakBytes = Math.Max(_peakBytes, dedicatedBytes);
        _currentValue.Text = FormatBytes(dedicatedBytes);
        _peakValue.Text = FormatBytes(_peakBytes);
        _averageValue.Text = FormatBytes(_sampleTotalBytes / _sampleCount);
        _processSharedValue.Text = FormatBytes(sharedBytes);
        _lastProcessMemory = _currentValue.Text;
        _lastProcessShared = _processSharedValue.Text;
    }

    private void SampleSelectedGpu(GpuSnapshot snapshot)
    {
        var dedicated = snapshot.AdapterDedicatedBytes;
        var shared = snapshot.AdapterSharedBytes;
        var usage = snapshot.AdapterUsagePercent;
        _lastGpuName = "GPU unavailable";
        _lastGpuUsage = "—";
        _lastGpuMemory = "—";
        _lastGpuShared = "—";
        _lastGpuTemp = "—";
        _lastGpuClock = "—";
        _lastGpuDedicatedBytes = 0;

        if (_gpuSelector.SelectedItem is not GpuAdapterItem gpu)
        {
            _gpuUsageValue.Text = "—";
            _gpuMemoryValue.Text = "—";
            _gpuSharedValue.Text = "—";
            _gpuTempValue.Text = "—";
            _gpuClockValue.Text = "—";
            return;
        }

        _lastGpuName = gpu.Name;
        // Driver readings belong to the GPU the sample was taken for; right after a switch that is still the old one.
        var telemetryIsCurrent = snapshot.Request.Gpu == gpu.Luid;
        var telemetry = telemetryIsCurrent ? snapshot.Telemetry : default;
        var missingSensorText = telemetryIsCurrent ? NoDriverSensorText : "—";

        if (usage.TryGetValue(gpu.Luid, out var percent))
        {
            _lastGpuUsage = $"{percent:0.0}%";
            _gpuUsageValue.Text = _lastGpuUsage;
        }
        else if (telemetry.UsagePercent.HasValue)
        {
            _lastGpuUsage = $"{telemetry.UsagePercent.Value:0.0}%";
            _gpuUsageValue.Text = _lastGpuUsage;
        }
        else
        {
            _gpuUsageValue.Text = "Unavailable";
        }

        if (dedicated.TryGetValue(gpu.Luid, out var dedicatedBytes))
        {
            _lastGpuDedicatedBytes = dedicatedBytes;
            _lastGpuMemory = FormatBytes(dedicatedBytes);
            _gpuMemoryValue.Text = _lastGpuMemory;
        }
        else
        {
            _gpuMemoryValue.Text = "Unavailable";
        }

        if (shared.TryGetValue(gpu.Luid, out var sharedBytes))
        {
            _lastGpuShared = FormatBytes(sharedBytes);
            _gpuSharedValue.Text = _lastGpuShared;
        }
        else
        {
            _gpuSharedValue.Text = "Unavailable";
        }

        if (telemetry.TempCelsius.HasValue)
        {
            _lastGpuTemp = $"{telemetry.TempCelsius.Value:0}°C";
            _gpuTempValue.Text = _lastGpuTemp;
        }
        else
        {
            _gpuTempValue.Text = missingSensorText;
        }

        if (telemetry.CoreClockMhz.HasValue)
        {
            _lastGpuClock = $"{telemetry.CoreClockMhz.Value} MHz";
            _gpuClockValue.Text = _lastGpuClock;
        }
        else
        {
            _gpuClockValue.Text = missingSensorText;
        }
    }

    private void RefreshGpuList(GpuSnapshot? sample)
    {
        if (_isRefreshingGpus)
        {
            return;
        }

        var selected = (_gpuSelector.SelectedItem as GpuAdapterItem)?.Luid ?? _settings.SelectedGpu;
        var changed = _gpuSelector.Items.Count == 0;

        // Only inject raw PDH counter LUIDs once DXGI has answered and listed NO adapters.
        // The names were resolved on the sampler thread; D3DKMT is a driver call too.
        if (_adapterCatalogLoaded && _adapterLookup.Count == 0 && sample is not null)
        {
            foreach (var (luid, name) in sample.AdapterNames)
            {
                changed |= _adapterLookup.TryAdd(luid, new GpuAdapterItem(luid, name));
            }
        }

        if (!changed && _gpuSelector.Items.Count > 0)
        {
            return;
        }

        var adapters = _adapterLookup.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _isRefreshingGpus = true;
        _gpuSelector.BeginUpdate();
        try
        {
            _gpuSelector.Items.Clear();
            _gpuSelector.Items.AddRange(adapters.Cast<object>().ToArray());
            var match = FindAdapter(adapters, selected, _settings.SelectedGpuName);
            _gpuSelector.SelectedItem = match;
            if (match is not null && (match.Luid != _settings.SelectedGpu || match.Name != _settings.SelectedGpuName))
            {
                _settings.SetSelectedGpu(match.Luid, match.Name);
                RequestSave();
            }
        }
        finally
        {
            _gpuSelector.EndUpdate();
            _isRefreshingGpus = false;
        }
    }

    /// <summary>
    /// Resolves the saved GPU. A LUID is only unique until the next restart, so after a reboot
    /// the name decides, and only when exactly one adapter carries it; otherwise the first adapter.
    /// </summary>
    internal static GpuAdapterItem? FindAdapter(IReadOnlyList<GpuAdapterItem> adapters, GpuLuid? luid, string? name)
    {
        var byLuid = luid.HasValue ? adapters.FirstOrDefault(adapter => adapter.Luid == luid.Value) : null;
        if (byLuid is not null)
        {
            return byLuid;
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            var byName = adapters.Where(adapter => string.Equals(adapter.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
            if (byName.Count == 1)
            {
                return byName[0];
            }
        }

        return adapters.Count > 0 ? adapters[0] : null;
    }

    private void SelectGpu()
    {
        if (_isRefreshingGpus)
        {
            return;
        }

        var gpu = _gpuSelector.SelectedItem as GpuAdapterItem;
        _settings.SetSelectedGpu(gpu?.Luid, gpu?.Name);
        UpdateSampleRequest();
        RequestSave();
        _gpuUsageValue.Text = "—";
        _gpuMemoryValue.Text = "—";
    }

    /// <summary>
    /// Rebuilds the process list off the UI thread; enumerating a few hundred processes
    /// takes long enough to be felt as a stutter in the overlay if it runs inline.
    /// </summary>
    private async void RefreshProcesses()
    {
        if (_isLoadingProcesses)
        {
            return;
        }

        _isLoadingProcesses = true;
        try
        {
            var processes = await Task.Run(GetProcesses);
            if (IsDisposed || Disposing)
            {
                return;
            }

            _allProcesses = processes;

            // Reconcile against the unfiltered list: a monitored process hidden by the search or the
            // VRAM filter is still running, and must not be mistaken for a restart.
            var reconciled = _processTracker.Reconcile(processes, TryGetStartTime);
            UpdateSampleRequest();
            if (reconciled is TrackerEvent.Attached or TrackerEvent.Reattached && _processTracker.Current is { } process)
            {
                RememberProcessName(process.Name);
                ResetStatistics();
                SetStatus(reconciled == TrackerEvent.Attached
                    ? $"Monitoring {process.Name} (PID {process.Pid})."
                    : $"Reattached to restarted {process.Name} (PID {process.Pid}).");
            }

            ApplyProcessFilters();
        }
        catch (Exception ex)
        {
            Log.Error("The running process list could not be read.", ex);
            SetStatus($"The running process list could not be read: {ex.Message}");
        }
        finally
        {
            _isLoadingProcesses = false;
        }
    }

    private void ApplyProcessFilters()
    {
        if (_isRefreshingProcesses)
        {
            return;
        }

        var searchText = _searchBox.Text.Trim();
        var onlyVram = _vramFilterCheck.Checked;

        _filteredProcesses = _allProcesses.Where(p =>
        {
            if (onlyVram && !_activeVramPids.Contains(p.Pid))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(searchText) && !p.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }).ToList();

        // The monitored process stays in the picker even when the filters hide it, so the
        // selection never goes blank while Nook is still measuring it.
        var monitored = _processTracker.Current;
        var items = new List<object>(_filteredProcesses.Count + 1);
        if (monitored is not null && !_filteredProcesses.Contains(monitored))
        {
            items.Add(monitored);
        }

        items.AddRange(_filteredProcesses);

        _isRefreshingProcesses = true;
        _processSelector.BeginUpdate();
        try
        {
            _processSelector.Items.Clear();
            _processSelector.Items.AddRange(items.ToArray());
            _processSelector.SelectedItem = monitored;
        }
        finally
        {
            _processSelector.EndUpdate();
            _isRefreshingProcesses = false;
        }
    }

    private void SelectProcess()
    {
        if (_isRefreshingProcesses)
        {
            return;
        }

        if (_processSelector.SelectedItem is ProcessItem process)
        {
            AttachTo(process, true);
            SetStatus($"Monitoring {process.Name} (PID {process.Pid}).");
        }
        else
        {
            ClearProcessSelection();
        }
    }

    private void AttachTo(ProcessItem process, bool resetStatistics)
    {
        _processTracker.Attach(process, TryGetStartTime(process.Pid));
        UpdateSampleRequest();
        RememberProcessName(process.Name);
        if (resetStatistics)
        {
            ResetStatistics();
        }
    }

    /// <summary>Saves the monitored name so it is followed again after Nook restarts.</summary>
    private void RememberProcessName(string name)
    {
        if (_settings.SelectedProcessName != name)
        {
            _settings.SelectedProcessName = name;
            RequestSave();
        }
    }

    private void ClearProcessSelection()
    {
        _processTracker.Detach(forget: true);
        UpdateSampleRequest();
        if (_settings.SelectedProcessName is not null)
        {
            _settings.SelectedProcessName = null;
            RequestSave();
        }

        ResetStatistics();
    }

    private void ResetStatistics()
    {
        _sampleCount = 0;
        _sampleTotalBytes = 0;
        _peakBytes = 0;
        _currentValue.Text = "—";
        _peakValue.Text = "—";
        _averageValue.Text = "—";
    }

    private void ClearCurrentValues()
    {
        _currentValue.Text = "—";
        _processSharedValue.Text = "—";
        _gpuUsageValue.Text = "—";
        _gpuMemoryValue.Text = "—";
        _gpuSharedValue.Text = "—";
        _gpuTempValue.Text = "—";
        _gpuClockValue.Text = "—";
        _lastGpuUsage = "—";
        _lastGpuMemory = "—";
        _lastGpuShared = "—";
        _lastGpuTemp = "—";
        _lastGpuClock = "—";
        _lastGpuDedicatedBytes = 0;
        _lastProcessMemory = null;
        _lastProcessShared = null;
    }

    private void CreateOverlay()
    {
        _overlay = new OverlayForm { Locked = _settings.OverlayLocked };
        if (_settings.OverlayX != int.MinValue && _settings.OverlayY != int.MinValue)
        {
            _overlay.RestoreLocation(new Point(_settings.OverlayX, _settings.OverlayY));
        }
        else
        {
            _overlay.MoveToCorner(OverlayCorner.TopRight);
        }

        _overlay.LocationChanged += (_, _) => RequestSave();
        _overlayVisible.Checked = _settings.OverlayVisible;
        _overlayLocked.Checked = _settings.OverlayLocked;
        UpdateOverlay();
        if (_settings.OverlayVisible)
        {
            _overlay.Show();
        }
    }

    private void UpdateOverlay()
    {
        if (_overlay is null || !_settings.OverlayVisible)
        {
            return;
        }

        // An integrated GPU has no dedicated pool and a parked discrete GPU keeps almost
        // nothing in it, so the overlay reports whichever pool the memory is actually in.
        var useShared = _lastGpuDedicatedBytes == 0;
        _overlay.UpdateMetrics(
            _lastGpuName,
            _lastGpuUsage,
            useShared ? "SHARED" : "VRAM",
            useShared ? _lastGpuShared : _lastGpuMemory,
            _lastGpuTemp,
            _lastGpuClock,
            _processTracker.Current?.Name,
            useShared ? _lastProcessShared : _lastProcessMemory);
    }

    private void ToggleOverlayVisibility() => SetOverlayVisibility(!_overlayVisible.Checked);

    private void SetOverlayVisibility(bool visible)
    {
        if (_settings.OverlayVisible != visible)
        {
            _settings.OverlayVisible = visible;
            RequestSave();
        }

        if (_overlayVisible.Checked != visible)
        {
            _overlayVisible.Checked = visible;
        }

        if (_trayOverlayItem is not null)
        {
            _trayOverlayItem.Checked = visible;
        }

        if (_overlay is null) return;
        if (visible) _overlay.Show();
        else _overlay.Hide();
    }

    private void ToggleOverlayLock() => SetOverlayLock(!_overlayLocked.Checked);

    private void SetOverlayLock(bool locked)
    {
        if (_settings.OverlayLocked != locked)
        {
            _settings.OverlayLocked = locked;
            RequestSave();
        }

        if (_overlayLocked.Checked != locked)
        {
            _overlayLocked.Checked = locked;
        }

        if (_trayLockItem is not null)
        {
            _trayLockItem.Checked = locked;
        }

        if (_overlay is not null)
        {
            _overlay.Locked = locked;
            UpdateOverlay();
            SetStatus(locked ? "Overlay is click-through and locked." : "Overlay adjustment mode: drag it to any position.");
        }
    }

    private void ShowCornerMenu(Control anchor)
    {
        _cornerMenu.Show(anchor, new Point(0, anchor.Height));
    }

    private void AddCornerMenuItem(string text, OverlayCorner corner) =>
        _cornerMenu.Items.Add(text, null, (_, _) => _overlay?.MoveToCorner(corner));

    private void RequestSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveSettings()
    {
        _saveTimer.Stop();
        if (_overlay is not null)
        {
            _settings.OverlayX = _overlay.Left;
            _settings.OverlayY = _overlay.Top;
        }

        if (!SettingsStore.Save(_settings))
        {
            Log.Warn("Settings could not be written; changes will be retried on the next save.");
        }
    }

    private void ApplyTheme()
    {
        var dark = IsDarkModeEnabled();
        var colors = SystemInformation.HighContrast
            ? new UiColors(SystemColors.Control, SystemColors.Window, SystemColors.Control, SystemColors.ControlText, SystemColors.GrayText, SystemColors.WindowFrame, SystemColors.Highlight)
            : dark
                ? new UiColors(Color.FromArgb(11, 15, 23), Color.FromArgb(22, 31, 48), Color.FromArgb(32, 43, 64), Color.FromArgb(248, 250, 252), Color.FromArgb(148, 163, 184), Color.FromArgb(51, 65, 85), Color.FromArgb(0, 180, 240))
                : new UiColors(Color.FromArgb(246, 248, 251), Color.White, Color.FromArgb(238, 242, 247), Color.FromArgb(23, 34, 49), Color.FromArgb(88, 104, 124), Color.FromArgb(196, 207, 222), Color.FromArgb(0, 120, 215));

        BackColor = colors.Background;
        ForeColor = colors.Foreground;
        ApplyTheme(this, colors);
        SwitchView(_processView.Visible ? "Process" : _gpuView.Visible ? "GPU" : "Settings");
    }

    private static void ApplyTheme(Control control, UiColors colors)
    {
        foreach (Control child in control.Controls)
        {
            if (child.Tag as string == "AccentBadge")
            {
                child.BackColor = colors.Accent;
                child.ForeColor = Color.White;
            }
            else if (child.Tag as string == "AccentText")
            {
                child.ForeColor = colors.Accent;
                child.BackColor = colors.Background;
            }
            else if (child.Tag as string == "MetricCard")
            {
                child.BackColor = colors.Surface;
                child.ForeColor = colors.Foreground;
            }
            else if (child.Tag as string == "ActionButton" && child is Button button)
            {
                button.BackColor = colors.Elevated;
                button.ForeColor = colors.Foreground;
                button.FlatAppearance.BorderColor = colors.Border;
            }
            else if (child is Panel or TableLayoutPanel or FlowLayoutPanel)
            {
                child.BackColor = colors.Background;
                child.ForeColor = colors.Foreground;
            }
            else if (child is ComboBox combo)
            {
                combo.BackColor = colors.Surface;
                combo.ForeColor = colors.Foreground;
            }
            else if (child is TextBox text)
            {
                text.BackColor = colors.Surface;
                text.ForeColor = colors.Foreground;
            }
            else if (child is Label or CheckBox)
            {
                child.ForeColor = child.Tag as string == MutedTag ? colors.Muted : colors.Foreground;
                child.BackColor = child.Parent?.Tag as string == "MetricCard" ? colors.Surface : colors.Background;
            }

            ApplyTheme(child, colors);
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!IsDisposed && IsHandleCreated && (e.Category is UserPreferenceCategory.Color or UserPreferenceCategory.General))
        {
            BeginInvoke(ApplyTheme);
        }
    }

    private static bool IsDarkModeEnabled()
    {
        if (SystemInformation.HighContrast) return false;
        return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int light && light == 0;
    }

    private void SetStartWithWindows(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key is null)
            {
                SetStatus("The Windows startup entry could not be opened.");
                return;
            }

            if (enable)
            {
                // Unquoted paths containing spaces let Windows launch a neighbouring
                // executable instead — quote the value even though ours rarely needs it.
                key.SetValue("Nook", $"\"{Environment.ProcessPath ?? Application.ExecutablePath}\"");
            }
            else
            {
                key.DeleteValue("Nook", false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Warn($"The Windows startup entry could not be updated: {ex.Message}");
            SetStatus($"The Windows startup entry could not be updated: {ex.Message}");
        }
    }

    private void SetStatus(string text)
    {
        // Called once per sample; assigning an identical string still forces a repaint.
        if (!string.Equals(_statusLabel.Text, text, StringComparison.Ordinal))
        {
            _statusLabel.Text = text;
        }
    }

    private static List<ProcessItem> GetProcesses()
    {
        var result = new List<ProcessItem>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                result.Add(new ProcessItem(process.Id, process.ProcessName));
            }
            catch
            {
                // A process can exit while the list is being built.
            }
            finally
            {
                process.Dispose();
            }
        }

        result.Sort(static (left, right) =>
        {
            var byName = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : left.Pid.CompareTo(right.Pid);
        });

        return result;
    }

    /// <summary>
    /// Start time is read only for the process being attached to: Windows denies access for
    /// protected processes, and asking for every entry costs one exception per denial.
    /// </summary>
    internal static DateTime? TryGetStartTime(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return TryGetStartTime(process);
        }
        catch
        {
            return null;
        }
    }

    private static DateTime? TryGetStartTime(Process process)
    {
        try { return process.StartTime; }
        catch { return null; }
    }

    /// <summary>Returns the live name and start time of a PID, or null once no process holds it.</summary>
    internal static (string Name, DateTime? StartTime)? LookUpProcess(int pid, string expectedName)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return (process.ProcessName, TryGetStartTime(process));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch
        {
            // Not being able to read the process is not proof that it exited, so keep following it.
            return (expectedName, null);
        }
    }

    internal static string FormatBytes(double bytes)
    {
        const double megabyte = 1024d * 1024d;
        const double gigabyte = megabyte * 1024d;
        return bytes >= gigabyte ? $"{bytes / gigabyte:0.00} GB" : $"{bytes / megabyte:0.0} MB";
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    private readonly record struct UiColors(Color Background, Color Surface, Color Elevated, Color Foreground, Color Muted, Color Border, Color Accent);

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        private sealed class DarkColorTable : ProfessionalColorTable
        {
            public override Color MenuItemSelected => Color.Transparent;
            public override Color MenuItemSelectedGradientBegin => Color.Transparent;
            public override Color MenuItemSelectedGradientEnd => Color.Transparent;
            public override Color MenuItemBorder => Color.Transparent;
            public override Color MenuBorder => Color.FromArgb(51, 65, 85);
            public override Color ToolStripDropDownBackground => Color.FromArgb(17, 24, 39);
            public override Color ImageMarginGradientBegin => Color.FromArgb(17, 24, 39);
            public override Color ImageMarginGradientMiddle => Color.FromArgb(17, 24, 39);
            public override Color ImageMarginGradientEnd => Color.FromArgb(17, 24, 39);
            public override Color SeparatorDark => Color.FromArgb(45, 55, 72);
            public override Color SeparatorLight => Color.Transparent;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.Enabled)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
                using var path = CreateRoundedRectanglePath(rect, 4);
                using var brush = new SolidBrush(Color.FromArgb(40, 0, 225, 255));
                e.Graphics.FillPath(brush, path);
            }
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var checkRect = e.ImageRectangle;
            using var checkPen = new Pen(Color.FromArgb(0, 225, 255), 2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            var cx = checkRect.X + checkRect.Width / 2f;
            var cy = checkRect.Y + checkRect.Height / 2f;

            PointF p1 = new(cx - 4f, cy);
            PointF p2 = new(cx - 1f, cy + 3.5f);
            PointF p3 = new(cx + 5f, cy - 3.5f);

            e.Graphics.DrawLines(checkPen, [p1, p2, p3]);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var y = e.Item.Height / 2;
            using var pen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f);
            e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Color.FromArgb(248, 250, 252) : Color.FromArgb(203, 213, 225);
            base.OnRenderItemText(e);
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var diameter = radius * 2;
            var arc = new Rectangle(rect.X, rect.Y, diameter, diameter);

            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.X;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
