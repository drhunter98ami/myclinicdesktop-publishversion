using System.Windows;

namespace MyClinic;

public static class OverlayMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(OverlayMotion), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement element) return;
        if ((bool)e.OldValue) element.IsVisibleChanged -= VisibilityChanged;
        if ((bool)e.NewValue) element.IsVisibleChanged += VisibilityChanged;
    }
    private static void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if ((bool)e.NewValue) ChartMotion.FadeIn(element);
        else element.BeginAnimation(UIElement.OpacityProperty, null);
    }
}
