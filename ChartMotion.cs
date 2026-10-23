using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MyClinic;

internal static class ChartMotion
{
    public static void EnterSlice(FrameworkElement slice, double centerX, double centerY, int index)
    {
        var scale = new ScaleTransform(1, 1, centerX, centerY);
        slice.RenderTransform = scale;
        Run(scale, ScaleTransform.ScaleXProperty, 0.82, 1, index * 28, 520);
        Run(scale, ScaleTransform.ScaleYProperty, 0.82, 1, index * 28, 520);
        FadeIn(slice, index * 28);
    }

    public static void GrowBar(FrameworkElement bar, int index)
    {
        var scale = new ScaleTransform(1, 1, 0, bar.Height);
        bar.RenderTransform = scale;
        Run(scale, ScaleTransform.ScaleYProperty, 0, 1, index * 14, 600);
        InspectPoint(bar);
    }

    public static void RevealLine(FrameworkElement line, double width, double height)
    {
        var clip = new RectangleGeometry(new Rect(0, 0, width, height));
        line.Clip = clip;
        if (!SystemParameters.ClientAreaAnimation) return;
        var animation = new RectAnimation(new Rect(0, 0, 0, height), new Rect(0, 0, width, height), TimeSpan.FromMilliseconds(850))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
        animation.Completed += (_, _) => clip.BeginAnimation(RectangleGeometry.RectProperty, null);
        clip.BeginAnimation(RectangleGeometry.RectProperty, animation);
    }

    public static void EnterMarker(FrameworkElement marker, int index)
    {
        var entrance = new ScaleTransform(1, 1, marker.Width / 2, marker.Height / 2);
        var hover = new ScaleTransform(1, 1, marker.Width / 2, marker.Height / 2);
        marker.RenderTransform = new TransformGroup { Children = { entrance, hover } };
        Run(entrance, ScaleTransform.ScaleXProperty, 0, 1, 100 + index * 14, 400);
        Run(entrance, ScaleTransform.ScaleYProperty, 0, 1, 100 + index * 14, 400);
        marker.MouseEnter += (_, _) =>
        {
            To(hover, ScaleTransform.ScaleXProperty, 1.45);
            To(hover, ScaleTransform.ScaleYProperty, 1.45);
        };
        marker.MouseLeave += (_, _) =>
        {
            To(hover, ScaleTransform.ScaleXProperty, 1);
            To(hover, ScaleTransform.ScaleYProperty, 1);
        };
        InspectPoint(marker);
    }

    private static void InspectPoint(FrameworkElement point)
    {
        ToolTipService.SetInitialShowDelay(point, 0);
        ToolTipService.SetBetweenShowDelay(point, 0);
        point.MouseEnter += (_, _) => To(point, UIElement.OpacityProperty, 0.75);
        point.MouseLeave += (_, _) => To(point, UIElement.OpacityProperty, 1);
    }

    public static void FadeIn(UIElement element, int delay = 0) => Run(element, UIElement.OpacityProperty, 0, 1, delay, 350);

    public static void To(DependencyObject target, DependencyProperty property, double value)
    {
        // Repeated routed input events should not restart an animation with the same goal.
        double resting = target is UIElement ui ? (double)ui.GetAnimationBaseValue(property)
            : target is Animatable animatable ? (double)animatable.GetAnimationBaseValue(property)
            : (double)target.GetValue(property);
        if (Math.Abs(resting - value) < 0.0001) return;
        double from = (double)target.GetValue(property);
        // Set the resting value first so completed clocks can be released.
        target.SetValue(property, value);
        Run(target, property, from, value, 0, 160);
    }

    private static void Run(DependencyObject target, DependencyProperty property, double from, double to, int delay, int duration)
    {
        DoubleAnimationUsingKeyFrames? animation = null;
        if (SystemParameters.ClientAreaAnimation)
        {
            delay = Math.Min(delay, 420);
            animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if (delay > 0)
                animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay))));
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(to,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay + duration)), new KeySpline(0.16, 1, 0.3, 1)));
            animation.Completed += (_, _) =>
            {
                if (target is UIElement element) element.BeginAnimation(property, null);
                else if (target is Animatable animatable) animatable.BeginAnimation(property, null);
            };
        }
        if (target is UIElement element) element.BeginAnimation(property, animation);
        else if (target is Animatable animatable) animatable.BeginAnimation(property, animation);
    }
}

// One instance belongs to each rendered pie. No static subscriptions retain old charts.
internal sealed class ChartLegendInteraction
{
    private readonly Dictionary<string, FrameworkElement> _slices = new();
    private readonly Dictionary<string, Grid> _rows = new();
    private readonly Dictionary<string, TranslateTransform> _offsets = new();
    private readonly Dictionary<string, Point> _directions = new();
    private readonly Dictionary<string, string> _descriptions = new();
    private readonly TextBlock _summary;
    private readonly string _totalText;
    private readonly bool _canOpenDetails;

    public ChartLegendInteraction(TextBlock summary, string totalText, bool canOpenDetails)
        => (_summary, _totalText, _canOpenDetails) = (summary, totalText, canOpenDetails);

    public void AddSlice(string label, FrameworkElement slice, double angle, string description)
    {
        _slices[label] = slice;
        _descriptions[label] = description;
        _directions[label] = new Point(Math.Cos(angle * Math.PI / 180) * 5, Math.Sin(angle * Math.PI / 180) * 5);
        var offset = new TranslateTransform();
        _offsets[label] = offset;
        slice.RenderTransform = new TransformGroup { Children = { slice.RenderTransform, offset } };
        slice.Cursor = _canOpenDetails ? Cursors.Hand : Cursors.Arrow;
        ToolTipService.SetInitialShowDelay(slice, 0);
        slice.MouseEnter += (_, _) => Select(label);
        slice.MouseLeave += (_, _) => Select(null);
    }

    public void AddRow(string label, Grid row, Action openDetails, string description)
    {
        _rows[label] = row;
        _descriptions[label] = description;
        row.Background = Brushes.Transparent;
        row.Focusable = true;
        row.Cursor = _canOpenDetails ? Cursors.Hand : Cursors.Arrow;
        row.ToolTip = description;
        System.Windows.Automation.AutomationProperties.SetName(row, description);
        row.MouseEnter += (_, _) => Select(label);
        row.MouseLeave += (_, _) => Select(null);
        row.GotKeyboardFocus += (_, _) => Select(label);
        row.LostKeyboardFocus += (_, _) => Select(null);
        row.KeyDown += (_, e) =>
        {
            if (_canOpenDetails && e.Key is Key.Enter or Key.Space)
            {
                openDetails();
                e.Handled = true;
            }
        };
    }

    private void Select(string? label)
    {
        label ??= _rows.FirstOrDefault(entry => entry.Value.IsKeyboardFocusWithin).Key;
        foreach (var (name, slice) in _slices)
        {
            bool selected = name == label;
            ChartMotion.To(slice, UIElement.OpacityProperty, label == null || selected ? 1 : 0.45);
            ChartMotion.To(_offsets[name], TranslateTransform.XProperty, selected ? _directions[name].X : 0);
            ChartMotion.To(_offsets[name], TranslateTransform.YProperty, selected ? _directions[name].Y : 0);
        }
        foreach (var (name, row) in _rows)
        {
            bool selected = name == label;
            row.Background = selected ? row.TryFindResource("AppSidebarHoverBg") as Brush ?? Brushes.Transparent : Brushes.Transparent;
            ChartMotion.To(row, UIElement.OpacityProperty, label == null || selected ? 1 : 0.6);
        }
        _summary.Text = label != null && _descriptions.TryGetValue(label, out var description) ? description : _totalText;
    }
}
