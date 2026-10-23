using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MyClinic;

internal sealed record ChartDatum(string Date, double Amount, Point Position);

internal static class ChartInspector
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(Inspector), typeof(ChartInspector));
    public static void Attach(Canvas canvas, IReadOnlyList<ChartDatum> data, double top, double baseline)
    {
        (canvas.GetValue(StateProperty) as Inspector)?.Dispose();
        canvas.SetValue(StateProperty, new Inspector(canvas, data, top, baseline));
    }
    private sealed class Inspector : IDisposable
    {
        private readonly Canvas _canvas;
        private readonly IReadOnlyList<ChartDatum> _data;
        private readonly Line _line;
        private readonly Ellipse _marker;
        private readonly Border _tooltip;
        private readonly TextBlock _text;
        private readonly double _top, _baseline;
        private int _selected = -1;
        public Inspector(Canvas canvas, IReadOnlyList<ChartDatum> data, double top, double baseline)
        {
            _canvas = canvas; _data = data; _top = top; _baseline = baseline;
            canvas.Background = Brushes.Transparent; canvas.Focusable = true;
            AutomationProperties.SetName(canvas, "مخطط الدخل حسب الفترة");
            AutomationProperties.SetHelpText(canvas, "حرّك المؤشر لعرض التاريخ والقيمة، أو استخدم مفاتيح الأسهم بعد التركيز على المخطط.");
            Brush accent = canvas.TryFindResource("DashCalendarTodayNumFg") as Brush ?? Brushes.Teal;
            _line = new Line { Y1 = top, Y2 = baseline, Stroke = accent, StrokeThickness = 1.2, StrokeDashArray = new DoubleCollection { 4, 4 }, IsHitTestVisible = false };
            _marker = new Ellipse { Width = 16, Height = 16, Stroke = accent, StrokeThickness = 2, Fill = Brushes.Transparent, IsHitTestVisible = false };
            _text = new TextBlock { Foreground = canvas.TryFindResource("AppTextPrimary") as Brush ?? Brushes.Black, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FlowDirection = FlowDirection.RightToLeft, FontSize = 12 };
            _tooltip = new Border { Width = 210, Padding = new Thickness(9, 6, 9, 6), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = accent, Background = canvas.TryFindResource("AppCardBg") as Brush ?? Brushes.White, Child = _text, IsHitTestVisible = false };
            foreach (UIElement element in new UIElement[] { _line, _marker, _tooltip })
            {
                Panel.SetZIndex(element, 20); element.Visibility = Visibility.Collapsed; canvas.Children.Add(element);
            }
            canvas.MouseMove += Move; canvas.MouseLeave += Leave; canvas.KeyDown += Key;
            canvas.Unloaded += Unloaded; canvas.LostKeyboardFocus += LostFocus;
        }
        private void Move(object sender, MouseEventArgs e)
        {
            Point point = e.GetPosition(_canvas);
            if (point.Y < _top || point.Y > _baseline || point.X < 64 || point.X > _canvas.Width - 16) { Hide(); return; }
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
            Canvas.SetLeft(_marker, item.Position.X - 8); Canvas.SetTop(_marker, item.Position.Y - 8);
            Canvas.SetLeft(_tooltip, Math.Clamp(item.Position.X - 105, 64, _canvas.Width - 226));
            Canvas.SetTop(_tooltip, Math.Max(8, item.Position.Y - 64));
            _text.Text = $"{item.Date}\n{item.Amount:N0} ل.س";
            foreach (UIElement element in new UIElement[] { _line, _marker, _tooltip }) element.Visibility = Visibility.Visible;
            AutomationProperties.SetItemStatus(_canvas, $"{item.Date}: {item.Amount:N0} ل.س");
        }
        private void Hide()
        {
            foreach (UIElement element in new UIElement[] { _line, _marker, _tooltip }) element.Visibility = Visibility.Collapsed;
            AutomationProperties.SetItemStatus(_canvas, "");
        }
        private void Leave(object sender, MouseEventArgs e) { if (!_canvas.IsKeyboardFocusWithin) Hide(); }
        private void Unloaded(object sender, RoutedEventArgs e) => Hide();
        private void LostFocus(object sender, KeyboardFocusChangedEventArgs e) => Hide();
        public void Dispose()
        {
            _canvas.MouseMove -= Move; _canvas.MouseLeave -= Leave; _canvas.KeyDown -= Key;
            _canvas.Unloaded -= Unloaded; _canvas.LostKeyboardFocus -= LostFocus;
            foreach (UIElement element in new UIElement[] { _line, _marker, _tooltip }) _canvas.Children.Remove(element);
        }
    }
}
