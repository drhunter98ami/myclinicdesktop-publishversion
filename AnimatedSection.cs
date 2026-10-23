using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace MyClinic;

public sealed class AnimatedSection : Expander
{
    private Border? _host;
    private int _version;
    public AnimatedSection()
    {
        Expanded += (_, e) => { if (ReferenceEquals(e.Source, this)) ApplyState(true); };
        Collapsed += (_, e) => { if (ReferenceEquals(e.Source, this)) ApplyState(true); };
    }
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _host = GetTemplateChild("PART_SectionBody") as Border;
        ApplyState(false);
    }
    private void ApplyState(bool animate)
    {
        if (_host == null) return;
        int version = ++_version;
        double previous = _host.ActualHeight;
        _host.BeginAnimation(FrameworkElement.HeightProperty, null);
        if (!animate || !IsLoaded || !MotionPreferences.Enabled)
        {
            _host.Height = IsExpanded ? double.NaN : 0;
            _host.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        if (!IsExpanded && _host.IsKeyboardFocusWithin)
            (GetTemplateChild("PART_SectionHeader") as ToggleButton)?.Focus();
        _host.Visibility = Visibility.Visible;
        double target = 0;
        if (IsExpanded)
        {
            _host.Height = double.NaN;
            _host.Measure(new Size(Math.Max(1, ActualWidth), double.PositiveInfinity));
            target = _host.DesiredSize.Height;
        }
        _host.Height = target;
        var animation = new DoubleAnimation(previous, target, MotionPreferences.Duration(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            if (version != _version || _host == null) return;
            _host.BeginAnimation(FrameworkElement.HeightProperty, null);
            _host.Height = IsExpanded ? double.NaN : 0;
            if (!IsExpanded) _host.Visibility = Visibility.Collapsed;
        };
        _host.BeginAnimation(FrameworkElement.HeightProperty, animation);
    }
}
