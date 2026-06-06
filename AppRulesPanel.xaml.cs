using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Luminosity.Models;
using Luminosity.Services;
using Luminosity.Ui;

using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using HAlign = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
using ImageSource = System.Windows.Media.ImageSource;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using UserControl = System.Windows.Controls.UserControl;

namespace Luminosity;

public partial class AppRulesPanel : UserControl
{
    private SettingsService _settings = null!;
    private Func<IReadOnlyList<MonitorInfo>> _monitorsProvider = () => Array.Empty<MonitorInfo>();
    private Action? _onRulesChanged;
    private Action? _onClose;

    private string? _pickedProcess;     // lowercase exe name, no extension
    private AppRule? _editingRule;       // non-null when editing an existing rule

    private sealed class EditEntry
    {
        public required string MonitorKey { get; init; }
        public required ColorControl Control { get; init; }
        public bool Included { get; set; }
    }
    private readonly List<EditEntry> _editEntries = new();

    public AppRulesPanel()
    {
        InitializeComponent();
    }

    /// <summary>Wires dependencies once (called by the host window).</summary>
    public void Initialize(SettingsService settings, Func<IReadOnlyList<MonitorInfo>> monitorsProvider,
        Action onRulesChanged, Action onClose)
    {
        _settings = settings;
        _monitorsProvider = monitorsProvider;
        _onRulesChanged = onRulesChanged;
        _onClose = onClose;
    }

    /// <summary>Refreshes the list and resets the new-rule form each time the overlay opens.</summary>
    public void Open()
    {
        ResetForm();
        RefreshRules();
    }

    // ---- Existing rules list --------------------------------------------------------------

    private void RefreshRules()
    {
        RulesPanel.Children.Clear();
        var rules = _settings.Settings.AppRules;
        NoRulesText.Visibility = rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var rule in rules)
            RulesPanel.Children.Add(BuildRuleRow(rule));
    }

    private Border BuildRuleRow(AppRule rule)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // enable
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // icon
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // info
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // edit
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // delete

        var enable = new CheckBox { IsChecked = rule.Enabled, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Enable/disable" };
        enable.Checked += (_, _) => { rule.Enabled = true; _settings.Save(); _onRulesChanged?.Invoke(); };
        enable.Unchecked += (_, _) => { rule.Enabled = false; _settings.Save(); _onRulesChanged?.Invoke(); };
        Grid.SetColumn(enable, 0);

        var icon = GetIconForProcessName(rule.ProcessName);
        var iconImg = IconImage(icon, 22);
        iconImg.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(iconImg, 1);

        var info = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = rule.Name, Foreground = (Brush)FindResource("TextBrush"), FontWeight = FontWeights.SemiBold });
        info.Children.Add(new TextBlock
        {
            Text = $"{rule.ProcessName}.exe  ·  {rule.ControlCount} control{(rule.ControlCount == 1 ? "" : "s")}",
            Foreground = (Brush)FindResource("MutedBrush"),
            FontSize = 11,
        });
        Grid.SetColumn(info, 2);

        var edit = new Button { Content = "Edit", Style = (Style)FindResource("GhostButton"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        edit.Click += (_, _) => BeginEdit(rule);
        Grid.SetColumn(edit, 3);

        var del = new Button { Content = "Delete", Style = (Style)FindResource("GhostButton"), VerticalAlignment = VerticalAlignment.Center };
        del.Click += (_, _) =>
        {
            _settings.Settings.AppRules.Remove(rule);
            _settings.Save();
            if (ReferenceEquals(_editingRule, rule)) ResetForm();
            RefreshRules();
            _onRulesChanged?.Invoke();
        };
        Grid.SetColumn(del, 4);

        grid.Children.Add(enable);
        grid.Children.Add(iconImg);
        grid.Children.Add(info);
        grid.Children.Add(edit);
        grid.Children.Add(del);

        return new Border
        {
            Style = (Style)FindResource("Card"),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = grid,
        };
    }

    // ---- Running-apps picker --------------------------------------------------------------

    private void ChooseApp_Click(object sender, RoutedEventArgs e)
    {
        PopulateProcessList();
        ProcessListHost.Visibility = Visibility.Visible;
    }

    private void ShowAll_Changed(object sender, RoutedEventArgs e)
    {
        if (ProcessListHost.Visibility == Visibility.Visible)
            PopulateProcessList();
    }

    private void PopulateProcessList()
    {
        ProcessList.Children.Clear();
        bool showAll = ShowAllToggle.IsChecked == true;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<(string Proc, string Display, ImageSource? Icon)>();

        Process[] procs = Array.Empty<Process>();
        try { procs = Process.GetProcesses(); } catch { }

        foreach (var p in procs)
        {
            try
            {
                string name = p.ProcessName;
                if (string.IsNullOrWhiteSpace(name)) continue;
                string title = "";
                try { title = p.MainWindowTitle ?? ""; } catch { }
                if (!showAll && string.IsNullOrWhiteSpace(title)) continue;
                if (!seen.Add(name)) continue;

                string display = string.IsNullOrWhiteSpace(title) ? $"{name}.exe" : $"{name}.exe  —  {title}";
                var icon = GetIconForPath(GetProcessPath(p.Id));
                items.Add((name.ToLowerInvariant(), display, icon));
            }
            catch { }
            finally { p.Dispose(); }
        }

        foreach (var (proc, display, icon) in items.OrderBy(i => i.Display, StringComparer.OrdinalIgnoreCase))
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(IconImage(icon, 18));
            content.Children.Add(new TextBlock
            {
                Text = display,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(icon is null ? 0 : 8, 0, 0, 0),
            });

            var btn = new Button
            {
                Content = content,
                Style = (Style)FindResource("GhostButton"),
                HorizontalAlignment = HAlign.Stretch,
                HorizontalContentAlignment = HAlign.Left,
                Margin = new Thickness(0, 0, 0, 4),
            };
            btn.Click += (_, _) => SelectProcess(proc, display);
            ProcessList.Children.Add(btn);
        }

        if (ProcessList.Children.Count == 0)
            ProcessList.Children.Add(new TextBlock
            {
                Text = "No apps found. Tick \"Show all processes\".",
                Foreground = (Brush)FindResource("MutedBrush"),
                Margin = new Thickness(6),
            });
    }

    private void SelectProcess(string processName, string display)
    {
        _pickedProcess = processName;
        PickedLabel.Text = $"{processName}.exe";
        ProcessListHost.Visibility = Visibility.Collapsed;

        if (EditorSection.Visibility != Visibility.Visible)
        {
            // First selection for a new rule: seed the editor from current live values.
            if (string.IsNullOrWhiteSpace(RuleNameBox.Text))
                RuleNameBox.Text = FriendlyName(display, processName);
            BuildEditor(null);
            EditorSection.Visibility = Visibility.Visible;
        }
        SaveButton.IsEnabled = true;
    }

    private static string FriendlyName(string display, string proc)
    {
        int dash = display.IndexOf("—", StringComparison.Ordinal);
        if (dash > 0)
        {
            string title = display[(dash + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(title)) return title;
        }
        return proc;
    }

    // ---- Editor ---------------------------------------------------------------------------

    private void BeginEdit(AppRule rule)
    {
        _editingRule = rule;
        _pickedProcess = rule.ProcessName;
        PickedLabel.Text = $"{rule.ProcessName}.exe";
        RuleNameBox.Text = rule.Name;
        SectionTitle.Text = "EDIT RULE";
        SaveButton.Content = "Update rule";
        CancelEditButton.Visibility = Visibility.Visible;

        BuildEditor(rule);
        EditorSection.Visibility = Visibility.Visible;
        SaveButton.IsEnabled = true;
    }

    private void BuildEditor(AppRule? existing)
    {
        MonitorEditorHost.Children.Clear();
        _editEntries.Clear();

        foreach (var monitor in _monitorsProvider())
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = monitor.Title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            stack.Children.Add(new Border { Height = 10 });

            Dictionary<string, int>? savedMap = null;
            existing?.Values.TryGetValue(monitor.Key, out savedMap);

            foreach (var live in monitor.Controls)
            {
                bool included = savedMap is not null && savedMap.ContainsKey(live.Type.ToString());
                int seed = included ? savedMap![live.Type.ToString()] : live.Current;

                var clone = new ColorControl
                {
                    Type = live.Type, Name = live.Name, Min = live.Min, Max = live.Max,
                    Step = live.Step, Default = live.Default, Current = seed, Unit = live.Unit,
                };
                var entry = new EditEntry { MonitorKey = monitor.Key, Control = clone, Included = included };
                _editEntries.Add(entry);

                ColorRow.Add(stack, clone, onChanged: _ => { },
                    withInclude: true, included: included, onInclude: inc => entry.Included = inc);
            }

            MonitorEditorHost.Children.Add(new Border
            {
                Style = (Style)FindResource("Card"),
                Width = 300,
                Margin = new Thickness(0, 0, 16, 16),
                Child = stack,
            });
        }
    }

    // ---- Save / cancel / close ------------------------------------------------------------

    private void SaveRule_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pickedProcess)) return;

        string name = RuleNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = _pickedProcess;

        var values = new Dictionary<string, Dictionary<string, int>>();
        foreach (var entry in _editEntries.Where(en => en.Included))
        {
            if (!values.TryGetValue(entry.MonitorKey, out var map))
                values[entry.MonitorKey] = map = new Dictionary<string, int>();
            map[entry.Control.Type.ToString()] = entry.Control.Current;
        }

        if (values.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this)!, "Tick at least one control for this rule to change.",
                "App rules", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_editingRule is not null)
        {
            _editingRule.Name = name;
            _editingRule.ProcessName = _pickedProcess!;
            _editingRule.Values = values;
        }
        else
        {
            _settings.Settings.AppRules.Add(new AppRule
            {
                Name = name, ProcessName = _pickedProcess!, Enabled = true, Values = values,
            });
        }
        _settings.Save();

        ResetForm();
        RefreshRules();
        _onRulesChanged?.Invoke();   // apply/revert immediately if the app is already running
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e) => ResetForm();

    private void ResetForm()
    {
        _pickedProcess = null;
        _editingRule = null;
        PickedLabel.Text = "";
        RuleNameBox.Text = "";
        SectionTitle.Text = "NEW RULE";
        SaveButton.Content = "Save rule";
        SaveButton.IsEnabled = false;
        CancelEditButton.Visibility = Visibility.Collapsed;
        ProcessListHost.Visibility = Visibility.Collapsed;
        EditorSection.Visibility = Visibility.Collapsed;
        MonitorEditorHost.Children.Clear();
        _editEntries.Clear();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => _onClose?.Invoke();

    // ---- Executable icons -----------------------------------------------------------------

    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder buffer, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    private static readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Full path of a process's executable (robust across architectures / limited rights).</summary>
    private static string? GetProcessPath(int pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>Extracts (and caches) the associated icon for an executable path as a WPF image.</summary>
    private static ImageSource? GetIconForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (_iconCache.TryGetValue(path, out var cached)) return cached;

        ImageSource? src = null;
        try
        {
            using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (ico is not null)
            {
                src = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
            }
        }
        catch { /* protected/system exe — no icon */ }

        _iconCache[path] = src;
        return src;
    }

    /// <summary>Best-effort icon for a rule's process name (only if it's currently running).</summary>
    private static ImageSource? GetIconForProcessName(string name)
    {
        try
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    var icon = GetIconForPath(GetProcessPath(p.Id));
                    if (icon is not null) return icon;
                }
            }
        }
        catch { }
        return null;
    }

    private static Image IconImage(ImageSource? src, double size = 18) => new()
    {
        Source = src,
        Width = size,
        Height = size,
        VerticalAlignment = VerticalAlignment.Center,
        SnapsToDevicePixels = true,
    };
}
