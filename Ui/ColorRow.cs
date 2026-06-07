using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Luminosity.Models;

using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Panel = System.Windows.Controls.Panel;
using HAlign = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace Luminosity.Ui;

/// <summary>
/// Builds one labelled slider row (label · value · slider · ↺ reset) shared by the live monitor
/// cards and the per-app rule editor. Resources are resolved from the application dictionary so the
/// helper is window-agnostic.
/// </summary>
public static class ColorRow
{
    private static Brush B(string key) => (Brush)System.Windows.Application.Current.FindResource(key);
    private static Style S(string key) => (Style)System.Windows.Application.Current.FindResource(key);

    /// <summary>The per-control spectrum gradient — the slider track "shows what it does".</summary>
    private static Brush Spectrum(int type) => B(type switch
    {
        ControlType.Saturation => "SpectrumSaturation",
        ControlType.Brightness => "SpectrumBrightness",
        ControlType.Contrast => "SpectrumContrast",
        ControlType.Gamma => "SpectrumGamma",
        ControlType.Hue => "SpectrumHue",
        ControlType.Temperature => "SpectrumTemperature",
        _ => "AccentBrush",
    });

    /// <summary>
    /// Adds a control row to <paramref name="parent"/> and returns its Slider.
    /// <paramref name="onChanged"/> fires on every value change (also sets <c>control.Current</c>).
    /// When <paramref name="withInclude"/> is set, a leading checkbox toggles whether the row is
    /// "active" (enables/disables the slider) and reports via <paramref name="onInclude"/>.
    /// </summary>
    public static Slider Add(Panel parent, ColorControl control, Action<int> onChanged,
        bool withInclude = false, bool included = false, Action<bool>? onInclude = null)
    {
        var label = new TextBlock
        {
            Text = control.Name,
            Foreground = B("TextDimBrush"),
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
        };

        CheckBox? include = null;
        FrameworkElement leading = label;
        if (withInclude)
        {
            include = new CheckBox { IsChecked = included, VerticalAlignment = VerticalAlignment.Center };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            label.Margin = new Thickness(-2, 0, 0, 0); // checkbox already has its content gap
            sp.Children.Add(include);
            sp.Children.Add(label);
            leading = sp;
        }

        var valueText = new TextBlock
        {
            Text = $"{control.Current}{control.Unit}",
            Foreground = B("TextBrush"),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HAlign.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(leading, 0);
        Grid.SetColumn(valueText, 1);
        header.Children.Add(leading);
        header.Children.Add(valueText);

        var slider = new Slider
        {
            Minimum = control.Min,
            Maximum = control.Max,
            Value = control.Current,
            SmallChange = control.Step,
            LargeChange = Math.Max(control.Step, (control.Max - control.Min) / 20.0),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = Spectrum(control.Type),   // per-control spectrum track (see Theme.xaml)
            ToolTip = $"{control.Name}: {control.Min}…{control.Max} (default {control.Default}). " +
                      "Click ↺ to reset · scroll to nudge.",
        };

        var reset = new Button
        {
            Content = "↺",
            Style = S("IconButton"),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Reset to default",
        };
        reset.Click += (_, _) => slider.Value = control.Default;

        var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(slider, 0);
        Grid.SetColumn(reset, 1);
        row.Children.Add(slider);
        row.Children.Add(reset);

        slider.ValueChanged += (_, e) =>
        {
            int v = (int)Math.Round(e.NewValue);
            control.Current = v;
            valueText.Text = $"{v}{control.Unit}";
            onChanged(v);
        };
        slider.PreviewMouseWheel += (_, e) =>
        {
            slider.Value = Math.Clamp(slider.Value + Math.Sign(e.Delta) * control.Step, control.Min, control.Max);
            e.Handled = true;
        };

        if (withInclude && include is not null)
        {
            slider.IsEnabled = included;
            reset.IsEnabled = included;
            include.Checked += (_, _) => { slider.IsEnabled = true; reset.IsEnabled = true; onInclude?.Invoke(true); };
            include.Unchecked += (_, _) => { slider.IsEnabled = false; reset.IsEnabled = false; onInclude?.Invoke(false); };
        }

        parent.Children.Add(header);
        parent.Children.Add(row);
        return slider;
    }
}
