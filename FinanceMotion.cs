using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MyClinic;

public static class FinanceMotion
{
    public static readonly DependencyProperty HighlightIdProperty = DependencyProperty.RegisterAttached("HighlightId", typeof(int), typeof(FinanceMotion), new PropertyMetadata(0));
    public static int GetHighlightId(DependencyObject target) => (int)target.GetValue(HighlightIdProperty);
    public static void SetHighlightId(DependencyObject target, int value) => target.SetValue(HighlightIdProperty, value);
    public static readonly DependencyProperty RevealProperty = DependencyProperty.RegisterAttached("Reveal", typeof(bool), typeof(FinanceMotion), new PropertyMetadata(false, RevealChanged));
    public static bool GetReveal(DependencyObject target) => (bool)target.GetValue(RevealProperty);
    public static void SetReveal(DependencyObject target, bool value) => target.SetValue(RevealProperty, value);
    private static void RevealChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement element) return;
        if ((bool)e.OldValue) element.Loaded -= Revealed;
        if ((bool)e.NewValue) element.Loaded += Revealed;
    }
    private static void Revealed(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        Fade(element);
        if (!MotionPreferences.Enabled || element.ActualHeight <= 0) return;
        var clip = new RectangleGeometry(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        element.Clip = clip;
        clip.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(new Rect(0, 0, element.ActualWidth, 0),
            new Rect(0, 0, element.ActualWidth, element.ActualHeight), MotionPreferences.Duration(180)) { FillBehavior = FillBehavior.Stop });
    }
    public static void Fade(UIElement element, double ms = 180)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (!MotionPreferences.Enabled) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.6, 1, MotionPreferences.Duration(ms)) { FillBehavior = FillBehavior.Stop });
    }
    public static void Slide(FrameworkElement panel)
    {
        var translate = new TranslateTransform(); panel.RenderTransform = translate;
        if (!MotionPreferences.Enabled) return;
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(MotionPreferences.Distance(24), 0, MotionPreferences.Duration(230))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        Fade(panel, 230);
    }
    public static void MoveSelector(Border indicator, FrameworkElement button, Grid host)
    {
        if (button.ActualWidth <= 0) return;
        double x = Math.Min(button.TranslatePoint(new Point(), host).X, button.TranslatePoint(new Point(button.ActualWidth, 0), host).X);
        var translate = indicator.RenderTransform as TranslateTransform ?? new TranslateTransform();
        double previous = translate.X;
        indicator.RenderTransform = translate; indicator.Width = button.ActualWidth;
        translate.BeginAnimation(TranslateTransform.XProperty, null); translate.X = x;
        if (MotionPreferences.Enabled) translate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(previous, x, MotionPreferences.Duration(165)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
    public static void Highlight(DataGrid grid, int id)
    {
        grid.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            foreach (var item in grid.Items)
            {
                if (grid.ItemContainerGenerator.ContainerFromItem(item) is not DataGridRow row || GetHighlightId(row) != id) continue;
                Fade(row, 700);
                row.IsSelected = true;
            }
        }));
    }
    public static void StopTree(DependencyObject root)
    {
        if (root is UIElement element)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            if (element.RenderTransform is TranslateTransform translate) translate.BeginAnimation(TranslateTransform.XProperty, null);
            if (element.RenderTransform is RotateTransform rotate) rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            if (element.Clip is RectangleGeometry clip) clip.BeginAnimation(RectangleGeometry.RectProperty, null);
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) StopTree(VisualTreeHelper.GetChild(root, i));
    }
}
