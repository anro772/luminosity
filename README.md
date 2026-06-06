# Luminosity

A small Windows utility for adjusting color **per monitor** — a vibranceGUI-style tool that works
on **any GPU and any number of monitors**.

It uses a pluggable backend layer and picks the best one available:

| Backend | GPUs | Controls |
|---------|------|----------|
| **AMD Radeon (ADL)** | AMD | Saturation · Brightness · Contrast · Hue · Temperature |
| **Universal (gamma ramp)** | NVIDIA / Intel / any | Brightness · Contrast · Gamma · Temperature |

The AMD backend talks to the **AMD Display Library** (`atiadlxx.dll`, shipped with the driver) — the
same API behind AMD Adrenalin's Color sliders. The universal backend uses Windows GDI gamma ramps
(`SetDeviceGammaRamp`), which work everywhere but can't do true saturation/hue (those need
cross-channel mixing only vendor APIs expose). A future NVIDIA NVAPI backend can drop into the same
`IColorBackend` interface for full controls on NVIDIA.

## Features

- **Any number of monitors** — cards are generated dynamically and re-scanned automatically when you
  plug/unplug a display or change resolution. The window sizes itself to fit your monitors (up to 3
  across, then it wraps and scrolls) and remembers your last size/position.
- One card per monitor; every slider applies **live** as you drag. Unsupported controls are hidden.
- **Reset** per knob (↺ button beside each slider), per monitor, or **all monitors** at once.
- **Scroll-wheel nudge** — hover a slider and scroll to step it.
- **Per-app rules** — auto-apply chosen color values when a game/app is running, and revert to your
  normal settings when it closes. Pick the app from a running-apps list, tick the controls to change
  and set their targets. Runs in the background (tray) with negligible CPU.
- **Minimize to tray** — closing the window hides it to the system tray (right-click → Show / Exit).
- **Run on Windows startup** — launches minimized at login and re-applies your last settings.
- Settings + rules persist to `%APPDATA%\Luminosity\settings.json`.

## Per-app rules

Click **App rules…** in the footer:
1. **Choose running app** — pick your game from the list (tick *Show all processes* if it's not
   listed, e.g. a fullscreen title with no window title).
2. **Tick the controls** the rule should change (per monitor) and set their target values. Unticked
   controls and other monitors are left untouched.
3. **Save**. The app runs in the tray and watches for that process (~3 s poll). When it starts, your
   targets are applied and the main window locks with an "Active" banner; when it exits, your
   pre-game values are restored. Your saved baseline is never overwritten.

One rule is active at a time (first match wins). For this to work during gaming, keep Luminosity
running — enable **Run on Windows startup** so it's always watching.

## Install

Grab the latest from [**Releases**](https://github.com/anro772/luminosity/releases):

- **`Luminosity-Setup-x.y.z.exe`** — installer (per-user, no admin needed). Adds Start-menu shortcut
  and an uninstaller.
- **`Luminosity.exe`** — portable single-file build; just run it, no install.

Both are self-contained (the .NET runtime is bundled), so there's nothing else to install.

## Requirements

- Windows 10/11, 64-bit.
- For full controls (saturation/hue): an AMD Radeon GPU with the driver installed. Other GPUs get the
  universal gamma-ramp controls.

## Build / run from source

```powershell
dotnet build  -c Release          # build
dotnet run    -c Release          # run
```

## Produce the portable single-file exe

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `bin\Release\net9.0-windows\win-x64\publish\Luminosity.exe` (~162 MB, no .NET install needed).
For a tiny exe instead, drop `--self-contained true` (requires the .NET 9 Desktop Runtime on the
target machine).

## Notes

- Builds **x64-only** because the AMD backend binds the 64-bit `atiadlxx.dll`.
- If a monitor is switched to **HDR**, the AMD SDR color controls may be unavailable or have no
  visible effect — a driver limitation, not a bug.
- AMD color settings generally persist in the driver; gamma-ramp settings do **not** persist across
  reboot/sleep. Either way Luminosity re-applies saved values on launch and after a display change.

## Architecture

```
Backends/   IColorBackend (interface)
            ├─ AmdAdlBackend      AMD ADL — full 5 controls
            ├─ GammaRampBackend   universal GDI gamma ramps — any GPU
            └─ ColorService       picks the best backend; add new vendors here
Adl/        AdlNative — raw ADL P/Invoke + structs
Models/     MonitorInfo, ColorControl, ControlType (shared constants), AppRule
Services/   SettingsService (JSON: baseline + rules + window bounds),
            StartupService (HKCU Run key), AppWatcher (per-app process poll)
Ui/         ColorRow — shared slider-row builder (live cards + rule editor)
Styles/     Theme.xaml (dark UI)
App.xaml         entry point, tray icon, --minimized boot, owns AppWatcher
MainWindow       dynamic per-monitor cards, live-apply, reset, hotplug rescan,
                 rule apply/revert + UI lock, adaptive/persisted sizing
AppRulesWindow   manage per-app rules: running-apps picker + per-control target editor
```

### Adding a new backend (e.g. NVIDIA)

Implement `IColorBackend` (Name / Initialize / GetMonitors / SetColor), build `MonitorInfo`s with a
backend-specific `BackendTag` for routing, and add it to the candidate list in `ColorService`.
