using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Luminosity.Backends;
using Luminosity.Models;
using Luminosity.Services;
using Luminosity.Ui;
using Microsoft.Win32;

// UseWindowsForms pulls System.Windows.Forms into scope; lock these names to their WPF types.
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using HAlign = System.Windows.HorizontalAlignment;

namespace Luminosity;

public partial class MainWindow : Window
{
    private readonly ColorService _service;
    private readonly SettingsService _settings;

    private List<MonitorInfo> _monitors = new();
    private readonly List<Binding> _bindings = new();

    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _rescanTimer;
    private bool _building;
    private bool _suppressPersist;   // true while applying/reverting a per-app rule

    private AppRule? _activeRule;
    private readonly List<(Binding Binding, int Original)> _ruleSnapshot = new();

    /// <summary>Set by App: asks the watcher to re-evaluate rules immediately.</summary>
    public Action? RequestRuleRecheck { get; set; }

    /// <summary>Links one on-screen slider to its monitor + control.</summary>
    private sealed record Binding(MonitorInfo Monitor, ColorControl Control, Slider Slider);

    public MainWindow(ColorService service, SettingsService settings)
    {
        _service = service;
        _settings = settings;
        InitializeComponent();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _settings.Save(); };

        // Debounced re-scan when monitors are added/removed or resolution changes.
        _rescanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _rescanTimer.Tick += (_, _) => { _rescanTimer.Stop(); Rescan(); };
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        RulesOverlay.Initialize(_settings, () => _monitors,
            onRulesChanged: () => RequestRuleRecheck?.Invoke(),
            onClose: CloseRulesOverlay);

        StartupToggle.IsChecked = StartupService.IsEnabled();
        Rescan();
        RestoreOrSizeWindow();
    }

    // ---- Card construction ----------------------------------------------------------------

    private void Rescan()
    {
        _monitors = _service.GetMonitors().ToList();
        BuildCards();
        ApplySavedSettings();

        int n = _monitors.Count;
        SubtitleText.Text = $"{n} display{(n == 1 ? "" : "s")}  ·  {_service.BackendName}";
        StatusText.Text = n == 0 ? "No adjustable displays found." : "";

        // If a game rule was active when displays changed, re-apply it to the rebuilt cards.
        if (_activeRule is not null)
        {
            var rule = _activeRule;
            _activeRule = null;
            _ruleSnapshot.Clear();
            ApplyAppRule(rule);
        }
    }

    private void BuildCards()
    {
        _building = true;
        _bindings.Clear();
        MonitorsHost.Items.Clear();
        foreach (var monitor in _monitors)
            MonitorsHost.Items.Add(BuildCard(monitor));
        _building = false;
    }

    private Border BuildCard(MonitorInfo monitor)
    {
        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = monitor.Title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrWhiteSpace(monitor.SubLabel))
        {
            stack.Children.Add(new TextBlock
            {
                Text = monitor.SubLabel,
                Foreground = (Brush)FindResource("MutedBrush"),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        stack.Children.Add(new Border { Height = 12 }); // spacer

        foreach (var control in monitor.Controls)
        {
            var capturedMonitor = monitor;
            var capturedControl = control;
            var slider = ColorRow.Add(stack, control, v => OnLiveValueChanged(capturedMonitor, capturedControl, v));
            _bindings.Add(new Binding(monitor, control, slider));
        }

        var reset = new Button
        {
            Content = "Reset this monitor",
            Style = (Style)FindResource("GhostButton"),
            HorizontalAlignment = HAlign.Left,
            Margin = new Thickness(0, 4, 0, 0),
        };
        reset.Click += (_, _) => ResetMonitor(monitor);
        stack.Children.Add(reset);

        return new Border
        {
            Style = (Style)FindResource("Card"),
            Width = 340,
            Margin = new Thickness(0, 0, 16, 16),
            Child = stack,
        };
    }

    private void OnLiveValueChanged(MonitorInfo monitor, ColorControl control, int value)
    {
        if (_building) return;
        _service.SetColor(monitor, control, value);
        if (_suppressPersist) return;           // rule apply/revert — don't touch the saved baseline
        _settings.StoreValue(monitor.Key, control.Type, value);
        ScheduleSave();
    }

    // ---- Apply / reset --------------------------------------------------------------------

    /// <summary>Re-applies persisted baseline values to matching monitors.</summary>
    public void ApplySavedSettings()
    {
        foreach (var b in _bindings)
            if (_settings.TryGetValue(b.Monitor.Key, b.Control.Type, out int saved))
                b.Slider.Value = Math.Clamp(saved, b.Control.Min, b.Control.Max);
    }

    private void ResetMonitor(MonitorInfo monitor)
    {
        foreach (var b in _bindings.Where(b => ReferenceEquals(b.Monitor, monitor)))
            b.Slider.Value = b.Control.Default;
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var b in _bindings)
            b.Slider.Value = b.Control.Default;
        Flash("All monitors reset to default.");
    }

    // ---- Per-app rule apply / revert ------------------------------------------------------

    /// <summary>Snapshots current values, applies the rule's targets, and locks the UI.</summary>
    public void ApplyAppRule(AppRule rule)
    {
        if (_activeRule is not null) return; // one at a time
        _activeRule = rule;
        _ruleSnapshot.Clear();

        _suppressPersist = true;
        foreach (var b in _bindings)
        {
            if (!rule.Values.TryGetValue(b.Monitor.Key, out var map)) continue;
            if (!map.TryGetValue(b.Control.Type.ToString(), out int target)) continue;

            _ruleSnapshot.Add((b, b.Control.Current));
            b.Slider.Value = Math.Clamp(target, b.Control.Min, b.Control.Max);
        }
        _suppressPersist = false;

        SetControlsEnabled(false);
        RuleBannerText.Text = $"●  Active: {rule.Name} — colors managed automatically until it closes.";
        RuleBanner.Visibility = Visibility.Visible;
    }

    /// <summary>Restores the pre-rule snapshot and unlocks the UI.</summary>
    public void RevertAppRule()
    {
        if (_activeRule is null) return;

        _suppressPersist = true;
        foreach (var (b, original) in _ruleSnapshot)
            b.Slider.Value = Math.Clamp(original, b.Control.Min, b.Control.Max);
        _suppressPersist = false;

        _ruleSnapshot.Clear();
        _activeRule = null;
        SetControlsEnabled(true);
        RuleBanner.Visibility = Visibility.Collapsed;
    }

    private void SetControlsEnabled(bool enabled)
    {
        MonitorsHost.IsEnabled = enabled;
        ResetAllButton.IsEnabled = enabled;
    }

    // ---- App rules window -----------------------------------------------------------------

    private void AppRules_Click(object sender, RoutedEventArgs e)
    {
        RulesOverlay.Open();
        RulesOverlay.Visibility = Visibility.Visible;
    }

    private void CloseRulesOverlay() => RulesOverlay.Visibility = Visibility.Collapsed;

    // ---- Window sizing --------------------------------------------------------------------

    private void RestoreOrSizeWindow()
    {
        var (recW, recH) = RecommendedSize();
        var s = _settings.Settings;

        // Never smaller than what's needed to show everything; a larger saved size is kept.
        double width = recW, height = recH;
        if (s.WinWidth is double w && s.WinHeight is double h && w > 320 && h > 320)
        {
            width = Math.Max(w, recW);
            height = Math.Max(h, recH);
            if (s.WinLeft is double l && s.WinTop is double t && IsOnScreen(l, t))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = l;
                Top = t;
            }
        }

        var wa = SystemParameters.WorkArea;
        Width = Math.Min(width, wa.Width * 0.97);
        Height = Math.Min(height, wa.Height * 0.95);
    }

    private (double Width, double Height) RecommendedSize()
    {
        int n = Math.Max(1, _monitors.Count);
        int cols = Math.Min(n, 3);
        int rows = (int)Math.Ceiling(n / (double)cols);
        int maxControls = _monitors.Count == 0 ? 5 : _monitors.Max(m => m.Controls.Count);

        const double cardWidth = 340 + 16;   // card + right margin
        double rowHeight = 26 + 36;           // header + slider row
        double cardHeight = 44 + 12 + maxControls * rowHeight + 38 + 40; // title+spacer+rows+reset+padding

        double width = cols * cardWidth + 48 + 22;                 // window margin + scrollbar
        double height = rows * cardHeight + 70 + 110 + 48;          // header + footer + margins
        return (width, height);
    }

    private static bool IsOnScreen(double left, double top)
    {
        var wa = SystemParameters.VirtualScreenWidth > 0
            ? new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                       SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight)
            : SystemParameters.WorkArea;
        return left >= wa.Left - 50 && top >= wa.Top - 10
            && left < wa.Right - 100 && top < wa.Bottom - 50;
    }

    public void PersistWindowBounds()
    {
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (b.Width < 320 || b.Height < 320) return;
        var s = _settings.Settings;
        s.WinWidth = b.Width;
        s.WinHeight = b.Height;
        s.WinLeft = b.Left;
        s.WinTop = b.Top;
    }

    // ---- Misc -----------------------------------------------------------------------------

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.Invoke(() => { _rescanTimer.Stop(); _rescanTimer.Start(); });

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Flash(string message)
    {
        StatusText.Text = message;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        t.Tick += (_, _) => { t.Stop(); if (StatusText.Text == message) StatusText.Text = ""; };
        t.Start();
    }

    private void StartupToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_building) return;
        bool enabled = StartupToggle.IsChecked == true;
        StartupService.SetEnabled(enabled);
        _settings.Settings.RunOnStartup = enabled;
        ScheduleSave();
    }

    // Closing the window hides it to the tray instead of exiting.
    protected override void OnClosing(CancelEventArgs e)
    {
        PersistWindowBounds();
        _settings.Save();
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
