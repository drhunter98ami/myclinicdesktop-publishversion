using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MyClinic;

internal sealed record ChartDatum(string Date, double Amount, Point Position,
    double? PreviousAmount = null, string? PreviousDate = null, string Unit = "ل.س", string Series = "الدخل");

internal static class ChartInspector
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(Inspector), typeof(ChartInspector));
    public static void Detach(Canvas canvas)
    {
        (canvas.GetValue(StateProperty) as Inspector)?.Dispose();
        canvas.ClearValue(StateProperty);
    }
    public static void Attach(Canvas canvas, IReadOnlyList<ChartDatum> data, double top, double baseline,
        double left = 64, double? right = null, IReadOnlyList<FrameworkElement>? emphasis = null)
    {
        (canvas.GetValue(StateProperty) as Inspector)?.Dispose();
        canvas.SetValue(StateProperty, new Inspector(canvas, data, top, baseline, left, right, emphasis));
    }
    private sealed class Inspector : IDisposable
    {
        private readonly Canvas _canvas;
        private readonly IReadOnlyList<ChartDatum> _data;
        private readonly Line _line;
        private readonly Ellipse _marker;
        private readonly Border _tooltip;
        private readonly TextBlock _text;
        private readonly double _top, _baseline, _left, _right;
        private readonly IReadOnlyList<FrameworkElement> _emphasis;
        private int _selected = -1;
        public Inspector(Canvas canvas, IReadOnlyList<ChartDatum> data, double top, double baseline,
            double left, double? right, IReadOnlyList<FrameworkElement>? emphasis)
        {
            _canvas = canvas; _data = data; _top = top; _baseline = baseline;
            _left = left; _right = right ?? (double.IsNaN(canvas.Width) ? canvas.ActualWidth : canvas.Width) - 16;
            _emphasis = emphasis ?? Array.Empty<FrameworkElement>();
            canvas.Background = Brushes.Transparent; canvas.Focusable = true;
            AutomationProperties.SetName(canvas, $"مخطط {(data.FirstOrDefault()?.Series ?? "الإحصائيات")} حسب الفترة");
            AutomationProperties.SetHelpText(canvas, "حرّك المؤشر لعرض التاريخ والقيمة، أو استخدم مفاتيح الأسهم بعد التركيز على المخطط.");
            Brush accent = StatisticsDashboard.Brush(StatisticsDashboard.IsDark() ? "#2DD4BF" : "#2563EB");
            _line = new Line { Y1 = top, Y2 = baseline, Stroke = accent, StrokeThickness = 1.2, StrokeDashArray = new DoubleCollection { 4, 4 }, IsHitTestVisible = false };
            _marker = new Ellipse { Width = 18, Height = 18, Stroke = accent, StrokeThickness = 2, Fill = canvas.TryFindResource("AppCardBg") as Brush ?? Brushes.White, IsHitTestVisible = false };
            _marker.RenderTransform = new ScaleTransform(1, 1, 9, 9);
            _text = new TextBlock { Foreground = canvas.TryFindResource("AppTextPrimary") as Brush ?? Brushes.Black, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FlowDirection = FlowDirection.RightToLeft, FontSize = 12 };
            _tooltip = new Border { Width = Math.Min(285, _right - _left), Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = accent, Background = canvas.TryFindResource("AppCardBg") as Brush ?? Brushes.White, Child = _text, IsHitTestVisible = false };
            foreach (UIElement element in new UIElement[] { _line, _marker, _tooltip })
            {
                Panel.SetZIndex(element, 20); element.Visibility = Visibility.Collapsed; canvas.Children.Add(element);
            }
            canvas.MouseMove += Move; canvas.MouseLeave += Leave; canvas.KeyDown += Key;
            canvas.MouseLeftButtonDown += Focus;
            canvas.Unloaded += Unloaded; canvas.LostKeyboardFocus += LostFocus;
        }
        private void Move(object sender, MouseEventArgs e)
        {
            Point point = e.GetPosition(_canvas);
            if (point.Y < _top || point.Y > _baseline || point.X < _left || point.X > _right) { Hide(); return; }
            if (_data.Count == 0) return;
            int nearest = Enumerable.Range(0, _data.Count).MinBy(i => Math.Abs(_data[i].Position.X - point.X));
            Select(nearest);
        }
        private void Key(object sender, KeyEventArgs e)
        {
            if (_data.Count == 0) return;
            if (e.Key == System.Windows.Input.Key.Escape) Hide();
            else if (e.Key == System.Windows.Input.Key.Home) Select(0);
            else if (e.Key == System.Windows.Input.Key.End) Select(_data.Count - 1);
            else if (e.Key == System.Windows.Input.Key.Left) Select(Math.Max(0, _selected - 1));
            else if (e.Key == System.Windows.Input.Key.Right) Select(Math.Min(_data.Count - 1, _selected + 1));
            else return;
            e.Handled = true;
        }
        private void Select(int index)
        {
            if (_selected == index && _tooltip.Visibility == Visibility.Visible) return;
            _selected = index; ChartDatum item = _data[index];
            _line.X1 = _line.X2 = item.Position.X;
            bool visible = _tooltip.Visibility == Visibility.Visible;
            if (visible)
            {
                ChartMotion.To(_marker, Canvas.LeftProperty, item.Position.X - 9);
                ChartMotion.To(_marker, Canvas.TopProperty, item.Position.Y - 9);
            }
            else
            {
                Canvas.SetLeft(_marker, item.Position.X - 9); Canvas.SetTop(_marker, item.Position.Y - 9);
                _tooltip.Opacity = .75; ChartMotion.To(_tooltip, UIElement.OpacityProperty, 1);
            }
            var halo = (ScaleTransform)_marker.RenderTransform;
            halo.BeginAnimation(ScaleTransform.ScaleXProperty, null); halo.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            halo.ScaleX = halo.ScaleY = .85;
            ChartMotion.To(halo, ScaleTransform.ScaleXProperty, 1); ChartMotion.To(halo, ScaleTransform.ScaleYProperty, 1);
            Canvas.SetLeft(_tooltip, Math.Clamp(item.Position.X - _tooltip.Width / 2, _left, _right - _tooltip.Width));
            _text.Text = $"{item.Date}\n{item.Series}: {item.Amount:N0} {item.Unit}";
            if (item.PreviousAmount is double previous)
                _text.Text += $"\nالسابق ({item.PreviousDate}): {previous:N0} {item.Unit}\n{StatisticsAnalytics.Change(item.Amount, previous)}";
            _tooltip.Measure(new Size(_tooltip.Width, double.PositiveInfinity));
            Canvas.SetTop(_tooltip, Math.Clamp(item.Position.Y - _tooltip.DesiredSize.Height - 18, 2, Math.Max(2, _baseline - _tooltip.DesiredSize.Height)));
            foreach (UIElement element in new UIElement[] { _line, _marker, _tooltip }) element.Visibility = Visibility.Visible;
            foreach (var element in _emphasis) ChartMotion.To(element, UIElement.OpacityProperty, element.Tag is int tag && tag == index ? 1 : .5);
            ChartMotion.To(_marker, UIElement.OpacityProperty, 1);
            AutomationProperties.SetItemStatus(_canvas, _text.Text);
        }
        private void Hide()
        {
            _marker.BeginAnimation(Canvas.LeftProperty, null); _marker.BeginAnimation(Canvas.TopProperty, null);
            foreach (UIElement element in new UIElement[] { _line, _marker, _tooltip }) element.Visibility = Visibility.Collapsed;
            AutomationProperties.SetItemStatus(_canvas, "");
            foreach (var element in _emphasis) ChartMotion.To(element, UIElement.OpacityProperty, 1);
        }
        private void Leave(object sender, MouseEventArgs e) { if (!_canvas.IsKeyboardFocusWithin) Hide(); }
        private void Focus(object sender, MouseButtonEventArgs e) => _canvas.Focus();
        private void Unloaded(object sender, RoutedEventArgs e) => Hide();
        private void LostFocus(object sender, KeyboardFocusChangedEventArgs e) => Hide();
        public void Dispose()
        {
            _canvas.MouseMove -= Move; _canvas.MouseLeave -= Leave; _canvas.KeyDown -= Key;
            _canvas.MouseLeftButtonDown -= Focus;
            _canvas.Unloaded -= Unloaded; _canvas.LostKeyboardFocus -= LostFocus;
            foreach (UIElement element in new UIElement[] { _line, _marker, _tooltip }) _canvas.Children.Remove(element);
        }
    }
}
