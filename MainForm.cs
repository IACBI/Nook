using System.Drawing;
using Microsoft.Win32;

namespace Nook;

internal sealed partial class MainForm : Form
{
    // Only NVIDIA and AMD publish a temperature and clock API we can call. Intel's
    // integrated adapters report neither, so say why instead of just "Unavailable".
    private const string NoDriverSensorText = "No driver sensor";

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
    private readonly Label _hotkeyInfo = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Tag = AppTheme.MutedTag, Margin = new Padding(0, 0, 0, 16) };
    private readonly Label _shortcutHint = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Tag = AppTheme.MutedTag, Margin = new Padding(8, 8, 0, 0) };
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
    private readonly MemoryStatistics _statistics = new();
    private string _currentView = "Process";
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
        GlobalHotkeys.Unregister(Handle, HotKeyToggleOverlay);
        GlobalHotkeys.Unregister(Handle, HotKeyToggleLock);

        var overlayKeys = GlobalHotkeys.Format(_settings.HotkeyToggleOverlayModifiers, _settings.HotkeyToggleOverlayKey);
        var lockKeys = GlobalHotkeys.Format(_settings.HotkeyToggleLockModifiers, _settings.HotkeyToggleLockKey);
        var overlayOk = GlobalHotkeys.Register(Handle, HotKeyToggleOverlay, _settings.HotkeyToggleOverlayModifiers, _settings.HotkeyToggleOverlayKey);
        var lockOk = GlobalHotkeys.Register(Handle, HotKeyToggleLock, _settings.HotkeyToggleLockModifiers, _settings.HotkeyToggleLockKey);

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
        _shortcutHint.Text = GlobalHotkeys.FormatPair(
            _settings.HotkeyToggleOverlayModifiers, _settings.HotkeyToggleOverlayKey,
            _settings.HotkeyToggleLockModifiers, _settings.HotkeyToggleLockKey);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (IsHandleCreated)
            {
                GlobalHotkeys.Unregister(Handle, HotKeyToggleOverlay);
                GlobalHotkeys.Unregister(Handle, HotKeyToggleLock);
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
        if (m.Msg == GlobalHotkeys.WmHotKey)
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

    private void SetStartWithWindows(bool enable)
    {
        if (StartupEntry.Update(enable) is { } problem)
        {
            Log.Warn(problem);
            SetStatus(problem);
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

}
