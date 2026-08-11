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
    private readonly System.Windows.Forms.Timer _sampleTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _processRefreshTimer = new() { Interval = 10000 };
    private readonly AppSettings _settings = SettingsStore.Load();
    private readonly NotifyIcon _notifyIcon = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly GpuTelemetryEngine _telemetryEngine = new();

    private readonly Dictionary<GpuLuid, GpuAdapterItem> _adapterLookup = [];
    private readonly HashSet<int> _activeVramPids = [];

    private ToolStripMenuItem? _trayOverlayItem;
    private ToolStripMenuItem? _trayLockItem;
    private PdhGpuMemoryReader? _reader;
    private OverlayForm? _overlay;
    private List<ProcessItem> _allProcesses = [];
    private List<ProcessItem> _filteredProcesses = [];
    private bool _isLoadingProcesses;
    private int? _selectedPid;
    private string? _selectedProcessName;
    private DateTime? _selectedStartTime;
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

    public MainForm()
    {
        Text = "Nook";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 480);
        MinimumSize = new Size(540, 430);
        FormBorderStyle = FormBorderStyle.Sizable;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);

        _cornerMenu.Renderer = new DarkMenuRenderer();
        _trayMenu.Renderer = new DarkMenuRenderer();

        BuildLayout();
        InitializeTrayIcon();
        ApplyTheme();

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _sampleTimer.Tick += (_, _) => SampleCounters();
        _processRefreshTimer.Tick += (_, _) => RefreshProcesses();
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

        var overlayOk = RegisterHotKey(Handle, HotKeyToggleOverlay, _settings.HotkeyToggleOverlayModifiers, _settings.HotkeyToggleOverlayKey);
        var lockOk = RegisterHotKey(Handle, HotKeyToggleLock, _settings.HotkeyToggleLockModifiers, _settings.HotkeyToggleLockKey);

        if (!overlayOk || !lockOk)
        {
            SetStatus("One or more global overlay shortcuts are unavailable on this system.");
        }
    }

    private void OnShown(object? sender, EventArgs e)
    {
        CreateOverlay();
        BeginInvoke(InitializeMonitoring);
    }

    private void InitializeMonitoring()
    {
        RefreshProcesses();
        RefreshGpuList(null);
        try
        {
            _reader = new PdhGpuMemoryReader();
            SetStatus("Ready. Values update once per second.");
        }
        catch (Exception ex)
        {
            SetStatus($"Windows GPU counters unavailable: {ex.Message}");
        }

        _sampleTimer.Start();
        _processRefreshTimer.Start();
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
            _sampleTimer.Dispose();
            _processRefreshTimer.Dispose();
            _notifyIcon.Dispose();
            _trayMenu.Dispose();
            _overlay?.Dispose();
            _cornerMenu.Dispose();
            _toolTip.Dispose();
            _reader?.Dispose();
            _telemetryEngine.Dispose();
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
        var subtitle = new Label { Text = "Per-process GPU memory, load, temperature and clock", Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText, TextAlign = ContentAlignment.TopLeft };
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

        var shortcuts = new Label { Text = "Ctrl+Shift+V / L", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 8, 0, 0) };
        commands.Controls.AddRange([_overlayVisible, _overlayLocked, corners, shortcuts]);

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
        minimizeCheck.CheckedChanged += (_, _) => _settings.MinimizeToTray = minimizeCheck.Checked;

        var closeCheck = new CheckBox { Text = "Close button minimizes to system tray", AutoSize = true, Checked = _settings.CloseToTray, Margin = new Padding(0, 0, 0, 6) };
        closeCheck.CheckedChanged += (_, _) => _settings.CloseToTray = closeCheck.Checked;

        var startupCheck = new CheckBox { Text = "Start automatically with Windows", AutoSize = true, Checked = _settings.StartWithWindows, Margin = new Padding(0, 0, 0, 16) };
        startupCheck.CheckedChanged += (_, _) =>
        {
            _settings.StartWithWindows = startupCheck.Checked;
            SetStartWithWindows(startupCheck.Checked);
        };

        var hotkeyHeader = new Label { Text = "Global Hotkeys", Font = new Font("Segoe UI Semibold", 10f), AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        var hotkeyInfo = new Label { Text = "Toggle Overlay: Ctrl + Shift + V\nToggle Lock: Ctrl + Shift + L", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 16) };

        panel.Controls.AddRange([trayHeader, minimizeCheck, closeCheck, startupCheck, hotkeyHeader, hotkeyInfo]);
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
            _settings.CloseToTray = false;
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
        if (_settings.CloseToTray && e.CloseReason == CloseReason.UserClosing)
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

    private void SampleCounters()
    {
        if (_reader is null)
        {
            return;
        }

        if (!IsSelectedProcessIdentityCurrent())
        {
            ClearProcessSelection($"{_selectedProcessName} exited or was replaced. Select it again.");
        }

        try
        {
            var result = _reader.Read();
            if (!result.IsAvailable)
            {
                SetStatus(result.Error ?? "Windows GPU counters are unavailable.");
                ClearCurrentValues();
                UpdateOverlay();
                return;
            }

            var processSamples = result.ProcessDedicatedBytes!;
            var processShared = result.ProcessSharedBytes!;
            _activeVramPids.Clear();
            _activeVramPids.UnionWith(processSamples.Keys);
            _activeVramPids.UnionWith(processShared.Keys);
            RefreshGpuList(result);
            SampleSelectedProcess(processSamples, processShared);
            SampleSelectedGpu(result.AdapterDedicatedBytes!, result.AdapterSharedBytes!, result.AdapterUsagePercent!);
            if (_selectedPid.HasValue && processSamples.ContainsKey(_selectedPid.Value))
            {
                SetStatus($"Monitoring {_selectedProcessName} (PID {_selectedPid}).");
            }
            UpdateOverlay();
        }
        catch (Exception ex)
        {
            ClearCurrentValues();
            SetStatus($"Windows GPU counter could not be read: {ex.Message}");
            UpdateOverlay();
        }
    }

    private void SampleSelectedProcess(IReadOnlyDictionary<int, long> dedicated, IReadOnlyDictionary<int, long> shared)
    {
        _lastProcessMemory = null;
        _lastProcessShared = null;
        if (!_selectedPid.HasValue)
        {
            return;
        }

        var hasDedicated = dedicated.TryGetValue(_selectedPid.Value, out var dedicatedBytes);
        var hasShared = shared.TryGetValue(_selectedPid.Value, out var sharedBytes);
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

    private bool IsSelectedProcessIdentityCurrent()
    {
        if (!_selectedPid.HasValue || _selectedProcessName is null)
        {
            return true;
        }

        try
        {
            using var process = Process.GetProcessById(_selectedPid.Value);
            if (!string.Equals(process.ProcessName, _selectedProcessName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var startTime = TryGetStartTime(process);
            return !_selectedStartTime.HasValue || !startTime.HasValue || startTime.Value == _selectedStartTime.Value;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch
        {
            return true;
        }
    }

    private void SampleSelectedGpu(
        IReadOnlyDictionary<GpuLuid, long> dedicated,
        IReadOnlyDictionary<GpuLuid, long> shared,
        IReadOnlyDictionary<GpuLuid, double> usage)
    {
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
        var telemetry = _telemetryEngine.GetTelemetry(gpu.Name);

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
            _gpuTempValue.Text = NoDriverSensorText;
        }

        if (telemetry.CoreClockMhz.HasValue)
        {
            _lastGpuClock = $"{telemetry.CoreClockMhz.Value} MHz";
            _gpuClockValue.Text = _lastGpuClock;
        }
        else
        {
            _gpuClockValue.Text = NoDriverSensorText;
        }
    }

    private void RefreshGpuList(GpuReadResult? sample)
    {
        if (_isRefreshingGpus)
        {
            return;
        }

        var selected = (_gpuSelector.SelectedItem as GpuAdapterItem)?.Luid ?? _settings.SelectedGpu;
        var changed = _gpuSelector.Items.Count == 0;
        if (!_adapterCatalogLoaded)
        {
            foreach (var adapter in GpuAdapterCatalog.GetAdapters())
            {
                _adapterLookup[adapter.Luid] = adapter;
            }

            _adapterCatalogLoaded = true;
        }

        // Only inject raw PDH counter LUIDs if DXGI hardware enumeration returned NO adapters
        if (_adapterLookup.Count == 0 && sample.HasValue && sample.Value.AdapterDedicatedBytes is not null)
        {
            foreach (var luid in sample.Value.AdapterDedicatedBytes.Keys)
            {
                var name = GpuAdapterCatalog.GetAdapterName(luid);
                if (!name.StartsWith("LUID 0x", StringComparison.OrdinalIgnoreCase))
                {
                    changed |= _adapterLookup.TryAdd(luid, new GpuAdapterItem(luid, name));
                }
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
            _gpuSelector.SelectedItem = selected.HasValue
                ? adapters.FirstOrDefault(adapter => adapter.Luid == selected.Value)
                : adapters.FirstOrDefault();
        }
        finally
        {
            _gpuSelector.EndUpdate();
            _isRefreshingGpus = false;
        }
    }

    private void SelectGpu()
    {
        if (_isRefreshingGpus)
        {
            return;
        }

        _settings.SetSelectedGpu((_gpuSelector.SelectedItem as GpuAdapterItem)?.Luid);
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
            ApplyProcessFilters();
        }
        catch (Exception ex)
        {
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

        var previousPid = _selectedPid;
        var previousName = _selectedProcessName;

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

        _isRefreshingProcesses = true;
        _processSelector.BeginUpdate();
        try
        {
            _processSelector.Items.Clear();
            _processSelector.Items.AddRange(_filteredProcesses.Cast<object>().ToArray());

            ProcessItem? match = previousPid.HasValue
                ? _filteredProcesses.FirstOrDefault(item => item.Pid == previousPid && string.Equals(item.Name, previousName, StringComparison.OrdinalIgnoreCase))
                : FindUniqueProcessByName(_settings.SelectedProcessName);

            if (match is null && previousName is not null)
            {
                // The target restarted under a new PID: follow it, but only when the name
                // resolves to exactly one process, so we never guess between siblings.
                match = FindUniqueProcessByName(previousName);
                if (match is not null)
                {
                    AttachTo(match, true);
                    SetStatus($"Reattached to restarted {match.Name} (PID {match.Pid}).");
                }
            }

            if (match is not null && !previousPid.HasValue)
            {
                AttachTo(match, true);
            }

            _processSelector.SelectedItem = match;
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
            ClearProcessSelection(null);
        }
    }

    private void AttachTo(ProcessItem process, bool resetStatistics)
    {
        _selectedPid = process.Pid;
        _selectedProcessName = process.Name;
        _selectedStartTime = TryGetStartTime(process.Pid);
        _settings.SelectedProcessName = process.Name;
        if (resetStatistics)
        {
            ResetStatistics();
        }
    }

    private void ClearProcessSelection(string? status)
    {
        _selectedPid = null;
        _selectedProcessName = null;
        _selectedStartTime = null;
        ResetStatistics();
        if (status is not null)
        {
            SetStatus(status);
        }
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
            _selectedProcessName,
            useShared ? _lastProcessShared : _lastProcessMemory);
    }

    private void ToggleOverlayVisibility() => SetOverlayVisibility(!_overlayVisible.Checked);

    private void SetOverlayVisibility(bool visible)
    {
        _settings.OverlayVisible = visible;
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
        _settings.OverlayLocked = locked;
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

    private void SaveSettings()
    {
        if (_overlay is not null)
        {
            _settings.OverlayX = _overlay.Left;
            _settings.OverlayY = _overlay.Top;
        }

        SettingsStore.Save(_settings);
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
                child.ForeColor = child.ForeColor == SystemColors.GrayText ? colors.Muted : colors.Foreground;
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

    private ProcessItem? FindUniqueProcessByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var matches = _filteredProcesses.Where(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
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
    private static DateTime? TryGetStartTime(int pid)
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

    private static string FormatBytes(double bytes)
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

    private sealed record ProcessItem(int Pid, string Name)
    {
        public override string ToString() => $"{Name} (PID {Pid})";
    }

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
