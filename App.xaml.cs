using System.Threading;
using System.Windows;
using Luminosity.Backends;
using Luminosity.Services;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Luminosity;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\Luminosity.SingleInstance.v1";
    private const string ShowSignalName = @"Local\Luminosity.Show.v1";

    private ColorService _colors = null!;
    private SettingsService _settings = null!;
    private MainWindow _window = null!;
    private AppWatcher _watcher = null!;
    private Forms.NotifyIcon _tray = null!;
    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showRegistration;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance: if one is already running, signal it to show its window and exit.
        _singleInstance = new Mutex(initiallyOwned: true, MutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowSignalName, out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch { /* best effort */ }
            Shutdown();
            return;
        }

        // Listen for "show" pokes from future second-launch attempts.
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        _showRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showSignal, (_, _) => Dispatcher.Invoke(ShowWindow), null, Timeout.Infinite, executeOnlyOnce: false);

        bool startMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        _settings = new SettingsService();
        _settings.Load();

        _colors = new ColorService();
        if (!_colors.Initialize())
        {
            System.Windows.MessageBox.Show(
                "No adjustable displays were found.\n\n" +
                "Luminosity works with any GPU via gamma ramps, and with full controls on AMD " +
                "Radeon (ADL). Make sure a monitor is connected and your graphics driver is installed.",
                "Luminosity", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _window = new MainWindow(_colors, _settings);
        SetupTray();

        // Re-apply previously saved values to matching monitors on launch.
        _window.ApplySavedSettings();

        // Background per-app watcher (runs whether or not the window is visible).
        _watcher = new AppWatcher(() => _settings.Settings.AppRules);
        _watcher.RuleActivated += rule => Dispatcher.Invoke(() => _window.ApplyAppRule(rule));
        _watcher.RuleDeactivated += _ => Dispatcher.Invoke(() => _window.RevertAppRule());
        _window.RequestRuleRecheck = () => _watcher.CheckNow();
        _watcher.Start();

        if (!startMinimized)
            _window.Show();
    }

    private void SetupTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "Luminosity",
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => ShowWindow());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowWindow();
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/icon.ico");
            var stream = GetResourceStream(uri)?.Stream;
            if (stream is not null)
                return new Drawing.Icon(stream);
        }
        catch { /* fall through */ }
        return Drawing.SystemIcons.Application;
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true;   // bounce to front past foreground-lock, then release
        _window.Topmost = false;
    }

    public void ExitApp()
    {
        // If a game rule is active, restore the user's normal colors before quitting.
        _watcher?.Stop();
        if (_watcher?.Active is not null)
            _window.RevertAppRule();

        _window.PersistWindowBounds();
        _settings.Save();

        _watcher?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _colors.Dispose();

        _showRegistration?.Unregister(null);
        _showSignal?.Dispose();
        try { _singleInstance?.ReleaseMutex(); } catch { }
        _singleInstance?.Dispose();

        Shutdown();
    }
}
