using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MyClinic;

public static class ClinicInput
{
    public static string NormalizeDigits(string? input)
    {
        var text = new StringBuilder();
        foreach (char c in input ?? string.Empty)
        {
            int digit = CharUnicodeInfo.GetDecimalDigitValue(c);
            text.Append(digit >= 0 ? (char)('0' + digit) : c switch { '٫' => '.', '٬' => ',', _ => c });
        }
        return text.ToString().Trim();
    }

    public static int AgeOn(DateTime birth, DateTime today) =>
        today.Year - birth.Year - (today.Date < birth.Date.AddYears(today.Year - birth.Year) ? 1 : 0);

    public static string? AllergyValue(int state, string? details) => state switch
    {
        1 => "لا توجد حساسية معروفة",
        2 => string.IsNullOrWhiteSpace(details) ? null : details.Trim(),
        _ => null
    };

    public static int AllergyState(string? value) => string.IsNullOrWhiteSpace(value) ? 0 :
        value == "لا توجد حساسية معروفة" ? 1 : 2;

    public static string? ChronicDiseaseValue(int state, string? details) => state switch
    {
        1 => "لا توجد أمراض معروفة",
        2 => string.IsNullOrWhiteSpace(details) ? null : details.Trim(),
        _ => null
    };
    public static int ChronicDiseaseState(string? value) => string.IsNullOrWhiteSpace(value) ? 0 :
        value == "لا توجد أمراض معروفة" ? 1 : 2;

    // Existing accounts use SYP as the accounting currency; treatment prices may be USD.
    public static bool ValidPayment(decimal cost, decimal paid) => cost >= 0 && paid >= 0 && paid <= cost;
}

public interface IRecoverableClinicForm
{
    bool PreserveDraft();
}

public static class FormMotion
{
    public static readonly DependencyProperty RevealWhenVisibleProperty = DependencyProperty.RegisterAttached(
        "RevealWhenVisible", typeof(bool), typeof(FormMotion), new PropertyMetadata(false, RevealSettingChanged));
    public static bool GetRevealWhenVisible(DependencyObject target) => (bool)target.GetValue(RevealWhenVisibleProperty);
    public static void SetRevealWhenVisible(DependencyObject target, bool value) => target.SetValue(RevealWhenVisibleProperty, value);
    private static void RevealSettingChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement element) return;
        if ((bool)e.OldValue) element.IsVisibleChanged -= Revealed;
        if ((bool)e.NewValue) element.IsVisibleChanged += Revealed;
    }
    private static void Revealed(object sender, DependencyPropertyChangedEventArgs e)
    { if ((bool)e.NewValue) Reveal((FrameworkElement)sender); }

    public static void Reveal(FrameworkElement? element)
    {
        if (element == null || !MotionPreferences.Enabled) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.45, 1, MotionPreferences.Duration(180))
        { FillBehavior = FillBehavior.Stop });
    }

    public static void Highlight(Control control)
    {
        if (!MotionPreferences.Enabled) return;
        var brush = new SolidColorBrush(Colors.Transparent);
        control.SetCurrentValue(Control.BorderBrushProperty, brush);
        Color accent = (control.TryFindResource("DashCalendarTodayBorder") as SolidColorBrush)?.Color ?? Colors.Teal;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(accent, Colors.Transparent, MotionPreferences.Duration(650))
        { AutoReverse = false, FillBehavior = FillBehavior.Stop });
        // Clear only this temporary local brush, restoring the style's dynamic theme resource.
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = MotionPreferences.Duration(700) };
        timer.Tick += (_, _) => { timer.Stop(); if (ReferenceEquals(control.BorderBrush, brush)) control.ClearValue(Control.BorderBrushProperty); };
        timer.Start();
    }

    public static void OpenDetails(FrameworkElement source, FrameworkElement target)
    {
        if (!MotionPreferences.Enabled) return;
        Reveal(target);
        var offset = new TranslateTransform();
        target.RenderTransform = offset;
        offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, MotionPreferences.Duration(220))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        Reveal(source);
    }
}

/// <summary>Equal-width groups wrap into fewer columns without creating nested scroll areas.</summary>
public sealed class AdaptiveFormPanel : Panel
{
    public static readonly DependencyProperty MinimumColumnWidthProperty = DependencyProperty.Register(
        nameof(MinimumColumnWidth), typeof(double), typeof(AdaptiveFormPanel), new FrameworkPropertyMetadata(280d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double MinimumColumnWidth { get => (double)GetValue(MinimumColumnWidthProperty); set => SetValue(MinimumColumnWidthProperty, value); }
    private const double Gap = 16;
    protected override Size MeasureOverride(Size available)
    {
        var visible = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        double width = double.IsInfinity(available.Width) ? MinimumColumnWidth * Math.Max(1, visible.Count) : available.Width;
        int columns = Math.Max(1, Math.Min(visible.Count, (int)((width + Gap) / (MinimumColumnWidth + Gap))));
        double cell = Math.Max(0, (width - Gap * (columns - 1)) / columns);
        double total = 0, row = 0; int index = 0;
        foreach (UIElement child in visible)
        {
            child.Measure(new Size(cell, double.PositiveInfinity));
            row = Math.Max(row, child.DesiredSize.Height);
            if (++index % columns == 0) { total += row + Gap; row = 0; }
        }
        if (index % columns != 0) total += row + Gap;
        return new Size(width, Math.Max(0, total - Gap));
    }
    protected override Size ArrangeOverride(Size final)
    {
        var visible = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        int columns = Math.Max(1, Math.Min(visible.Count, (int)((final.Width + Gap) / (MinimumColumnWidth + Gap))));
        double cell = Math.Max(0, (final.Width - Gap * (columns - 1)) / columns), y = 0;
        for (int i = 0; i < visible.Count; i += columns)
        {
            double height = visible.Skip(i).Take(columns).Max(c => c.DesiredSize.Height);
            for (int j = 0; j < columns && i + j < visible.Count; j++)
                visible[i + j].Arrange(new Rect(j * (cell + Gap), y, cell, height));
            y += height + Gap;
        }
        return final;
    }
}
