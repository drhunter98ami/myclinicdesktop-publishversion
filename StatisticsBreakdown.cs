using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MyClinic;

public sealed class StatisticsBreakdown : UserControl
{
    private readonly Canvas _donut = new() { Width = 280, Height = 280, FlowDirection = FlowDirection.LeftToRight };
    private readonly StackPanel _legend = new(), _ranking = new();
    private readonly TextBlock _summary = StatisticsDashboard.Text("", 12), _center = StatisticsDashboard.Text("0", 22, true);
    private readonly TextBlock _unit = StatisticsDashboard.Text("", 12), _empty = StatisticsDashboard.Text("لا توجد بيانات لهذه الفترة", 14);
    private readonly Grid _body = new();
    private readonly StackPanel _donutPanel = new();
    private readonly Button _donutButton = new() { Content = "توزيع دائري" }, _rankButton = new() { Content = "ترتيب الفئات" };
    private List<StatisticsCategory> _categories = new();
    private bool _money, _ranked;
    private Action<string>? _openDetails;
    private ChartLegendInteraction? _interaction;

    public StatisticsBreakdown()
    {
        FlowDirection = FlowDirection.RightToLeft;
        var content = new StackPanel();
        content.Children.Add(StatisticsDashboard.Text("توزيع الفئات", 19, true));
        var toolbar = new WrapPanel { Margin = new Thickness(0, 12, 0, 14) };
        foreach (var button in new[] { _donutButton, _rankButton })
        {
            button.Padding = new Thickness(14, 7, 14, 7); button.Margin = new Thickness(0, 0, 6, 0); button.Cursor = Cursors.Hand;
            button.SetResourceReference(BackgroundProperty, "AppInputBg"); button.SetResourceReference(ForegroundProperty, "AppTextPrimary");
            button.SetResourceReference(BorderBrushProperty, "AppBorder"); toolbar.Children.Add(button);
            StatisticsDashboard.StyleButton(button);
        }
        _donutButton.Click += (_, _) => { _ranked = false; Render(); };
        _rankButton.Click += (_, _) => { _ranked = true; Render(); };
        content.Children.Add(toolbar);
        _empty.HorizontalAlignment = HorizontalAlignment.Center; _empty.Margin = new Thickness(0, 10, 0, 14); content.Children.Add(_empty);
        var ring = new Grid { Width = 280, Height = 280 };
        ring.Children.Add(_donut);
        var center = new StackPanel { Width = 146, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        var totalLabel = StatisticsDashboard.Text("الإجمالي", 12); totalLabel.TextAlignment = TextAlignment.Center; center.Children.Add(totalLabel);
        _center.TextAlignment = TextAlignment.Center; _center.TextWrapping = TextWrapping.NoWrap;
        center.Children.Add(new Viewbox { Child = _center, StretchDirection = StretchDirection.DownOnly, MaxHeight = 36, Margin = new Thickness(0, 6, 0, 4) });
        _unit.TextAlignment = TextAlignment.Center; center.Children.Add(_unit); ring.Children.Add(center);
        _donutPanel.Children.Add(ring); _summary.Margin = new Thickness(0, 10, 0, 10); _summary.TextAlignment = TextAlignment.Center; _donutPanel.Children.Add(_summary);
        _body.Children.Add(_donutPanel); _body.Children.Add(_legend);
        content.Children.Add(_body); content.Children.Add(_ranking);
        Content = StatisticsDashboard.Card(content, 22);
        SizeChanged += (_, _) => ArrangeBody();
    }

    internal void ShowData(IEnumerable<StatisticsCategory> categories, bool money, Action<string>? openDetails)
    {
        _categories = categories.Where(c => c.Value > 0).OrderByDescending(c => c.Value).ThenBy(c => c.Label).ToList();
        _money = money; _openDetails = openDetails; Render();
    }
    private string Format(double value) => _money ? $"{value:N0} ل.س" : $"{value:N0}";
    private void ArrangeBody()
    {
        bool wide = ActualWidth >= 720;
        _body.ColumnDefinitions.Clear(); _body.RowDefinitions.Clear();
        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = wide ? new GridLength(310) : new GridLength(1, GridUnitType.Star) });
        if (wide) _body.ColumnDefinitions.Add(new ColumnDefinition());
        _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (!wide) _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(_legend, wide ? 1 : 0); Grid.SetRow(_legend, wide ? 0 : 1);
        _legend.Margin = new Thickness(wide ? 18 : 0, 0, 0, 0);
    }
    private void Render()
    {
        _donut.Children.Clear(); _legend.Children.Clear(); _ranking.Children.Clear();
        _body.Visibility = _ranked ? Visibility.Collapsed : Visibility.Visible; _ranking.Visibility = _ranked ? Visibility.Visible : Visibility.Collapsed;
        _donutButton.FontWeight = _ranked ? FontWeights.Normal : FontWeights.Bold; _rankButton.FontWeight = _ranked ? FontWeights.Bold : FontWeights.Normal;
        double total = _categories.Sum(c => c.Value);
        _empty.Visibility = total <= 0 ? Visibility.Visible : Visibility.Collapsed;
        _unit.Text = _money ? "ل.س" : "العدد"; AnimatedNumber.Show(_center, total, "{0:N0}");
        string summary = $"{_categories.Count:N0} فئات · {Format(total)}";
        _summary.Text = summary; _interaction = new ChartLegendInteraction(_summary, summary, _openDetails != null);
        double angle = -90;
        int index = 0;
        foreach (var category in _categories)
        {
            double sweep = Math.Min(359.9999, category.Value / total * 360);
            var path = Slice(angle, sweep, StatisticsDashboard.Brush(category.Color));
            _donut.Children.Add(path); ChartMotion.EnterSlice(path, 140, 140, index);
            string description = $"{category.Label}: {Format(category.Value)} ({category.Value / total:P1})";
            _interaction.AddSlice(category.Label, path, angle + sweep / 2, description);
            if (_openDetails != null) path.MouseLeftButtonUp += (_, _) => _openDetails(category.Label);
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3), MinHeight = 44 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var swatch = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(3), Background = StatisticsDashboard.Brush(category.Color), HorizontalAlignment = HorizontalAlignment.Right };
            row.Children.Add(swatch);
            var label = StatisticsDashboard.Text(category.Label, 13, true); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(label, 1); row.Children.Add(label);
            var value = StatisticsDashboard.Text($"{Format(category.Value)}\n{category.Value / total:P1}", 12); value.TextAlignment = TextAlignment.Left; value.VerticalAlignment = VerticalAlignment.Center; value.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(value, 2); row.Children.Add(value);
            _interaction.AddRow(category.Label, row, () => _openDetails?.Invoke(category.Label), description);
            if (_openDetails != null) row.MouseLeftButtonUp += (_, _) => _openDetails(category.Label);
            _legend.Children.Add(row);
            _legend.Children.Add(new Border { Height = 1, Background = StatisticsDashboard.Resource("AppBorder"), Opacity = .45 });
            AddRank(category, total, _categories[0].Value, index++);
            angle += sweep;
        }
        ArrangeBody();
    }
    private void AddRank(StatisticsCategory category, double total, double maximum, int index)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 14), Background = Brushes.Transparent, Focusable = true, Cursor = _openDetails != null ? Cursors.Hand : Cursors.Arrow };
        var labels = new Grid(); labels.ColumnDefinitions.Add(new ColumnDefinition()); labels.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = StatisticsDashboard.Text($"{index + 1:00}  {category.Label}", 13, true); name.Margin = new Thickness(12, 0, 0, 0); labels.Children.Add(name);
        var amount = StatisticsDashboard.Text($"{Format(category.Value)}  ·  {category.Value / total:P1}", 12); Grid.SetColumn(amount, 1); labels.Children.Add(amount); panel.Children.Add(labels);
        var track = new Border { Height = 9, CornerRadius = new CornerRadius(4), Background = StatisticsDashboard.Resource("AppInputBg"), Margin = new Thickness(0, 10, 0, 0), ClipToBounds = true };
        var bar = new Border { Height = 9, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Right, Background = StatisticsDashboard.Brush(category.Color) };
        track.Child = bar; track.SizeChanged += (_, _) => bar.Width = Math.Max(0, track.ActualWidth * category.Value / maximum);
        var scale = new ScaleTransform(1, 1); bar.RenderTransform = scale; bar.RenderTransformOrigin = new Point(1, .5);
        if (MotionPreferences.Enabled) scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, MotionPreferences.Duration(450)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        panel.Children.Add(track); AutomationProperties.SetName(panel, $"{category.Label}: {Format(category.Value)}, {category.Value / total:P1}");
        panel.ToolTip = _openDetails == null ? $"{category.Label}: {Format(category.Value)}" : "عرض تفاصيل المصاريف";
        panel.MouseEnter += (_, _) => ChartMotion.To(panel, OpacityProperty, .75); panel.MouseLeave += (_, _) => ChartMotion.To(panel, OpacityProperty, 1);
        if (_openDetails != null)
        {
            panel.MouseLeftButtonUp += (_, _) => _openDetails(category.Label);
            panel.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { _openDetails(category.Label); e.Handled = true; } };
        }
        _ranking.Children.Add(panel);
    }
    private static System.Windows.Shapes.Path Slice(double start, double sweep, Brush brush)
    {
        Point At(double radius, double angle) => new(140 + radius * Math.Cos(angle * Math.PI / 180), 140 + radius * Math.Sin(angle * Math.PI / 180));
        var figure = new PathFigure { StartPoint = At(125, start), IsClosed = true };
        figure.Segments.Add(new ArcSegment(At(125, start + sweep), new Size(125, 125), 0, sweep > 180, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(At(85, start + sweep), true));
        figure.Segments.Add(new ArcSegment(At(85, start), new Size(85, 85), 0, sweep > 180, SweepDirection.Counterclockwise, true));
        return new System.Windows.Shapes.Path { Data = new PathGeometry(new[] { figure }), Fill = brush, Stroke = StatisticsDashboard.Resource("AppCardBg"), StrokeThickness = 2 };
    }
}
