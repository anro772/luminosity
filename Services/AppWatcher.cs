using System.Diagnostics;
using System.Windows.Threading;
using Luminosity.Models;

namespace Luminosity.Services;

/// <summary>
/// Watches running processes and raises events when a watched per-app rule's process appears or
/// disappears. Lightweight: a single ~3 s timer that calls <see cref="Process.GetProcessesByName"/>
/// for each enabled rule. One rule is active at a time (first match wins).
/// </summary>
public sealed class AppWatcher : IDisposable
{
    private readonly Func<IReadOnlyList<AppRule>> _rulesProvider;
    private readonly DispatcherTimer _timer;

    private AppRule? _active;

    /// <summary>Raised on the UI thread when a watched process starts.</summary>
    public event Action<AppRule>? RuleActivated;

    /// <summary>Raised on the UI thread when the active rule's process exits.</summary>
    public event Action<AppRule>? RuleDeactivated;

    public AppRule? Active => _active;

    public AppWatcher(Func<IReadOnlyList<AppRule>> rulesProvider, TimeSpan? interval = null)
    {
        _rulesProvider = rulesProvider;
        _timer = new DispatcherTimer { Interval = interval ?? TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start()
    {
        Poll();        // immediate first check (catches an already-running game)
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    /// <summary>Re-evaluates immediately (e.g. after a rule is enabled/disabled/deleted).</summary>
    public void CheckNow() => Poll();

    private void Poll()
    {
        var rules = _rulesProvider();

        // If a rule is active, it stays active only while it's still enabled, still exists, and its
        // process is still running. Otherwise deactivate now — then fall through to find a new match.
        if (_active is not null)
        {
            bool stillValid = _active.Enabled && rules.Contains(_active) && IsRunning(_active.ProcessName);
            if (stillValid)
                return;

            var ended = _active;
            _active = null;
            RuleDeactivated?.Invoke(ended);
        }

        foreach (var rule in rules)
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.ProcessName))
                continue;
            if (IsRunning(rule.ProcessName))
            {
                _active = rule;
                RuleActivated?.Invoke(rule);
                return;
            }
        }
    }

    private static bool IsRunning(string processName)
    {
        // GetProcessesByName expects the name without ".exe".
        string name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
        Process[] procs = Array.Empty<Process>();
        try
        {
            procs = Process.GetProcessesByName(name);
            return procs.Length > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            foreach (var p in procs) p.Dispose();
        }
    }

    public void Dispose() => _timer.Stop();
}
