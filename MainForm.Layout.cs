using Microsoft.Win32;

namespace Nook;

internal sealed partial class MainForm
{
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
        var subtitle = new Label { Text = "Per-process GPU memory, load, temperature and clock", Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText, Tag = AppTheme.MutedTag, TextAlign = ContentAlignment.TopLeft };
        var cadence = new Label { Text = "WINDOWS-NATIVE  •  1 SECOND", AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Font = new Font("Segoe UI Semibold", 8f), Padding = new Padding(10, 5, 10, 5), Tag = AppTheme.AccentBadgeTag };
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
        _statusLabel.Tag = AppTheme.MutedTag;
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
        Tag = AppTheme.NavButtonTag
    };

    private void SwitchView(string viewName)
    {
        _currentView = viewName;
        _processView.Visible = viewName == "Process";
        _gpuView.Visible = viewName == "GPU";
        _settingsView.Visible = viewName == "Settings";

        AppTheme.StyleNavButton(_navProcessBtn, viewName == "Process");
        AppTheme.StyleNavButton(_navGpuBtn, viewName == "GPU");
        AppTheme.StyleNavButton(_navSettingsBtn, viewName == "Settings");
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
        ForeColor = AppTheme.Accent,
        Font = new Font("Segoe UI Semibold", 8.5f),
        TextAlign = ContentAlignment.MiddleLeft,
        UseMnemonic = false, // otherwise "&" is swallowed as an access-key prefix
        Tag = AppTheme.AccentTextTag
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
        button.Tag = AppTheme.ActionButtonTag;
        button.AccessibleName = button.Text;
        button.MouseEnter += (_, _) => button.BackColor = AppTheme.Accent;
        button.MouseLeave += (_, _) => AppTheme.StyleActionButtonIdle(button, AppTheme.Current());
        button.MouseDown += (_, _) => button.BackColor = AppTheme.AccentPressed;
        button.MouseUp += (_, _) => button.BackColor = AppTheme.Accent;
        _toolTip.SetToolTip(button, toolTip);
    }

    private static TableLayoutPanel CreateMetricsPanel((string Name, Label Value)[] metrics)
    {
        var rowCount = metrics.Length;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rowCount, Margin = new Padding(0, 4, 0, 0) };
        for (var row = 0; row < rowCount; row++)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rowCount));
            var card = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, row == 0 ? 0 : 6, 0, 0), Padding = new Padding(14, 0, 14, 0), Tag = AppTheme.MetricCardTag };
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

    private void ApplyTheme()
    {
        var colors = AppTheme.Current();
        BackColor = colors.Background;
        ForeColor = colors.Foreground;
        AppTheme.Apply(this, colors);
        // Not read back from Visible: before the window is shown every panel reports false.
        SwitchView(_currentView);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!IsDisposed && IsHandleCreated && (e.Category is UserPreferenceCategory.Color or UserPreferenceCategory.General))
        {
            BeginInvoke(ApplyTheme);
        }
    }

}
