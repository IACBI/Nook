using Microsoft.Win32;

namespace Nook;

internal sealed partial class MainForm
{
    private const int MetricCardHeight = 40;
    private const int MetricCardGap = 6;

    private static readonly string[] ViewOrder = ["Process", "GPU", "Settings"];

    private readonly List<Label> _valueLabels = [];
    private ThemePalette _palette = AppTheme.Current();

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 14, 18, 10),
            ColumnCount = 1,
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); // Header
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); // Nav Bar
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Content
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); // Status Bar
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // Bottom Controls

        // Header Layout
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label { Text = "Nook", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 15.5f), TextAlign = ContentAlignment.BottomLeft, AccessibleName = "Application title" };
        // AutoEllipsis: a narrow window would otherwise wrap the subtitle onto a second, clipped line.
        var subtitle = new Label { Text = "Per-process GPU memory, load, temperature and clock", Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText, Tag = AppTheme.MutedTag, TextAlign = ContentAlignment.TopLeft, AutoEllipsis = true };
        var cadence = new Label { Text = "WINDOWS-NATIVE  ·  1 SECOND", AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Font = new Font("Segoe UI Semibold", 7.5f), Padding = new Padding(8, 4, 8, 4), Margin = new Padding(12, 0, 0, 2), Tag = AppTheme.AccentBadgeTag };
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(cadence, 1, 0);
        // Spans the badge column too, so the full subtitle fits at the default width.
        header.Controls.Add(subtitle, 0, 1);
        header.SetColumnSpan(subtitle, 2);

        // Segmented Nav Bar
        var navBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 6, 0, 10)
        };

        _navProcessBtn.Click += (_, _) => SwitchView("Process");
        _navGpuBtn.Click += (_, _) => SwitchView("GPU");
        _navSettingsBtn.Click += (_, _) => SwitchView("Settings");
        _toolTip.SetToolTip(_navProcessBtn, "Per-process GPU memory (Ctrl+1)");
        _toolTip.SetToolTip(_navGpuBtn, "Adapter load, memory and sensors (Ctrl+2)");
        _toolTip.SetToolTip(_navSettingsBtn, "Tray, startup and shortcuts (Ctrl+3)");
        navBar.Controls.AddRange([_navProcessBtn, _navGpuBtn, _navSettingsBtn]);

        // Views
        BuildProcessView(_processView);
        BuildGpuView(_gpuView);
        BuildSettingsView(_settingsView);

        _contentContainer.Margin = Padding.Empty;
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
        _overlayVisible.Margin = new Padding(0, 9, 16, 0);
        _overlayVisible.AccessibleName = "Show overlay";
        _overlayVisible.CheckedChanged += (_, _) => SetOverlayVisibility(_overlayVisible.Checked);

        _overlayLocked.AutoSize = true;
        _overlayLocked.Margin = new Padding(0, 9, 16, 0);
        _overlayLocked.AccessibleName = "Lock overlay click-through mode";
        _overlayLocked.CheckedChanged += (_, _) => SetOverlayLock(_overlayLocked.Checked);

        var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty, AccessibleName = "Overlay controls" };
        var corners = CreateActionButton("Position  ▾", "Move the overlay to a screen corner");
        corners.AccessibleName = "Overlay position";
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

    private static ThemedButton CreateNavButton(string text) => new()
    {
        Text = text,
        Size = new Size(96, 30),
        Margin = new Padding(0, 0, 6, 0),
        Font = new Font("Segoe UI Semibold", 9f),
        Tag = AppTheme.NavButtonTag
    };

    private void SwitchView(string viewName)
    {
        _currentView = viewName;
        var views = new[] { ("Process", _processView, _navProcessBtn), ("GPU", _gpuView, _navGpuBtn), ("Settings", _settingsView, _navSettingsBtn) };

        // The new view is shown before the old one is hidden, so the content area never paints empty in between.
        foreach (var (name, view, _) in views)
        {
            if (name == viewName) view.Visible = true;
        }

        foreach (var (name, view, button) in views)
        {
            var active = name == viewName;
            view.Visible = active;
            AppTheme.StyleNavButton(button, active, _palette);
            button.AccessibleDescription = active ? "Current view" : null;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var index = Array.IndexOf(ViewOrder, _currentView);
        var target = keyData switch
        {
            Keys.Control | Keys.D1 => 0,
            Keys.Control | Keys.D2 => 1,
            Keys.Control | Keys.D3 => 2,
            Keys.Control | Keys.Tab => (index + 1) % ViewOrder.Length,
            Keys.Control | Keys.Shift | Keys.Tab => (index + ViewOrder.Length - 1) % ViewOrder.Length,
            _ => -1
        };

        if (target < 0)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        SwitchView(ViewOrder[target]);
        return true;
    }

    private void BuildProcessView(Panel container)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); // Filter Row
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); // Selection Row
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // Source Label Row
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Metrics Panel Row

        // Filter / Search Row
        var filterRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        var searchLabel = new Label { Text = "Search", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9f) };
        _searchBox.Dock = DockStyle.Fill;
        _searchBox.Margin = new Padding(0, 5, 12, 5);
        _searchBox.BorderStyle = BorderStyle.FixedSingle;
        _searchBox.PlaceholderText = "Filter by process name…";
        _searchBox.AccessibleName = "Filter processes by name";
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
        StyleActionButton(_refreshButton, "Reload the list of running processes");
        _refreshButton.Click += (_, _) => RefreshProcesses();

        var source = CreateSourceLabel("Windows-reported GPU memory for the selected process");
        var metrics = CreateMetricsPanel(new[]
        {
            ("Dedicated (current)", _currentValue),
            ("Dedicated (peak)", _peakValue),
            ("Dedicated (average)", _averageValue),
            ("Shared (current)", _processSharedValue),
            ("Shared (peak)", _processSharedPeakValue)
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // Source Label Row
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
            ("Temperature", _gpuTempValue),
            ("Core clock", _gpuClockValue)
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
            // No side padding, so section headers line up with the labels on the other views.
            Padding = new Padding(3, 8, 0, 4)
        };

        var trayHeader = CreateSectionHeader("System tray and startup");
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

        var startupCheck = new CheckBox { Text = "Start automatically with Windows", AutoSize = true, Checked = _settings.StartWithWindows, Margin = new Padding(0, 0, 0, 20) };
        startupCheck.CheckedChanged += (_, _) =>
        {
            _settings.StartWithWindows = startupCheck.Checked;
            SetStartWithWindows(startupCheck.Checked);
            RequestSave();
        };

        var hotkeyHeader = CreateSectionHeader("Global shortcuts");

        panel.Controls.AddRange([trayHeader, minimizeCheck, closeCheck, startupCheck, hotkeyHeader, _hotkeyInfo]);
        container.Controls.Add(panel);
    }

    private static Label CreateSectionHeader(string text) => new()
    {
        Text = text,
        Font = new Font("Segoe UI Semibold", 10f),
        AutoSize = true,
        Margin = new Padding(0, 0, 0, 8)
    };

    private static TableLayoutPanel CreateSelectionRow(string labelText, ComboBox selector, ThemedButton? action)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = action is null ? 2 : 3, RowCount = 1, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (action is not null)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        }

        var label = new Label { Text = labelText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9f) };
        selector.Dock = DockStyle.Fill;
        // Flat lets the closed DropDownList take the theme's colours instead of a white system face.
        selector.FlatStyle = FlatStyle.Flat;
        selector.Margin = new Padding(0, 5, action is null ? 0 : 12, 3);
        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(selector, 1, 0);
        if (action is not null)
        {
            action.Dock = DockStyle.Fill;
            // Same top and height as the combo box beside it.
            action.Margin = new Padding(0, 5, 0, 8);
            layout.Controls.Add(action, 2, 0);
        }

        return layout;
    }

    private static Label CreateSourceLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI Semibold", 8.5f),
        TextAlign = ContentAlignment.MiddleLeft,
        UseMnemonic = false, // otherwise "&" is swallowed as an access-key prefix
        Tag = AppTheme.AccentTextTag
    };

    private ThemedButton CreateActionButton(string text, string toolTip)
    {
        var button = new ThemedButton { Text = text, AutoSize = false, Size = new Size(104, 30), Margin = new Padding(0, 5, 14, 5) };
        StyleActionButton(button, toolTip);
        return button;
    }

    private void StyleActionButton(ThemedButton button, string toolTip)
    {
        button.Tag = AppTheme.ActionButtonTag;
        button.AccessibleName = button.Text;
        button.AccessibleDescription = toolTip;
        AppTheme.StyleActionButton(button, _palette);
        _toolTip.SetToolTip(button, toolTip);
    }

    /// <summary>
    /// Fixed-height cards stacked from the top. Stretching them to fill the view made them balloon in a
    /// maximized window and crushed the values in a small one; the panel scrolls if they do not fit.
    /// </summary>
    private TableLayoutPanel CreateMetricsPanel((string Name, Label Value)[] metrics)
    {
        var rowCount = metrics.Length;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rowCount + 1, Margin = new Padding(0, 2, 0, 0), AutoScroll = true };
        for (var row = 0; row < rowCount; row++)
        {
            var gap = row == 0 ? 0 : MetricCardGap;
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, MetricCardHeight + gap));
            var card = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, gap, 0, 0), Padding = new Padding(14, 0, 14, 0), Tag = AppTheme.MetricCardTag };
            // Muted and regular so the value, not its caption, is what the eye lands on.
            var label = new Label { Text = metrics[row].Name, Dock = DockStyle.Left, Width = 200, TextAlign = ContentAlignment.MiddleLeft, Tag = AppTheme.MutedTag, AccessibleName = $"{metrics[row].Name} label" };
            var value = metrics[row].Value;
            value.AccessibleName = metrics[row].Name;
            value.TextChanged += (_, _) => StyleValueLabel(value);
            _valueLabels.Add(value);
            card.Controls.Add(value);
            card.Controls.Add(label);
            panel.Controls.Add(card, 0, row);
        }

        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return panel;
    }

    private static Label CreateValueLabel() => new()
    {
        Dock = DockStyle.Fill,
        Text = "—",
        TextAlign = ContentAlignment.MiddleRight,
        Font = new Font("Segoe UI Semibold", 14f),
        AutoEllipsis = true
    };

    /// <summary>
    /// Readings always contain a digit; "—", "Unavailable" and "No driver sensor" do not. Those are
    /// shown muted so a missing sensor does not read as loudly as a real number.
    /// </summary>
    private void StyleValueLabel(Label value)
    {
        var color = value.Text.Any(char.IsAsciiDigit) ? _palette.Foreground : _palette.Muted;
        if (value.ForeColor != color)
        {
            value.ForeColor = color;
        }
    }

    private void ApplyTheme()
    {
        _palette = AppTheme.Current();
        BackColor = _palette.Background;
        ForeColor = _palette.Foreground;
        AppTheme.Apply(this, _palette);
        foreach (var value in _valueLabels)
        {
            StyleValueLabel(value);
        }

        // Owner-drawn dark menus only belong to a dark window; light and high-contrast use the system look.
        ToolStripRenderer menuRenderer = _palette.IsDark ? new DarkMenuRenderer() : new ToolStripProfessionalRenderer();
        _cornerMenu.Renderer = menuRenderer;
        _trayMenu.Renderer = menuRenderer;
        AppTheme.ApplyTitleBar(this, _palette.IsDark);

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
