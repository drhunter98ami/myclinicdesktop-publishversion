using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace MyClinic;

public static class AnimatedNumber
{
    private static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached("Value", typeof(double), typeof(AnimatedNumber), new PropertyMetadata(0d, ValueChanged));
    private static readonly DependencyProperty FormatProperty = DependencyProperty.RegisterAttached("Format", typeof(string), typeof(AnimatedNumber), new PropertyMetadata("{0:N0}"));
    private static readonly DependencyProperty InitializedProperty = DependencyProperty.RegisterAttached("Initialized", typeof(bool), typeof(AnimatedNumber));
    public static void Show(TextBlock label, double value, string format)
    {
        bool initialized = (bool)label.GetValue(InitializedProperty);
        double previous = (double)label.GetValue(ValueProperty);
        label.BeginAnimation(ValueProperty, null);
        label.SetValue(FormatProperty, format);
        label.SetValue(InitializedProperty, true);
        label.SetValue(ValueProperty, value);
        // The authoritative total is always the resting value, including when motion is off.
        label.Text = string.Format(CultureInfo.CurrentCulture, format, value);
        if (initialized && label.IsLoaded && previous != value && MotionPreferences.Enabled)
        {
            var animation = new DoubleAnimation(previous, value, MotionPreferences.Duration(420))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
            };
            animation.Completed += (_, _) => label.BeginAnimation(ValueProperty, null);
            label.BeginAnimation(ValueProperty, animation);
            ChartMotion.Emphasize(label);
        }
    }
    public static void Cancel(TextBlock label) => label.BeginAnimation(ValueProperty, null);
    private static void ValueChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is TextBlock label)
            label.Text = string.Format(CultureInfo.CurrentCulture, (string)label.GetValue(FormatProperty), (double)e.NewValue);
    }
}
