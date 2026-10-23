using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MyClinic;

/// <summary>Responsive analytics surface shared by the modern and original interface.</summary>
public sealed class StatisticsDashboard : UserControl
{
    private readonly Grid _cards = new();
    private readonly Canvas _plot = new() { Height = 330, ClipToBounds = true, FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBlock _title = Text("", 21, true), _subtitle = Text("", 12), _note = Text("", 12);
    private readonly TextBlock[] _values = new TextBlock[4], _hints = new TextBlock[4];
    private readonly TextBlock[] _labels = new TextBlock[4];
    private readonly Canvas _sparkline = new() { Height = 28, FlowDirection = FlowDirection.LeftToRight, IsHitTestVisible = false };
    private readonly CheckBox _compare = new() { Content = "الفترة السابقة", IsChecked = true, Margin = new Thickness(10, 8, 10, 8) };
    private readonly CheckBox _average = new() { Content = "المتوسط", IsChecked = true, Margin = new Thickness(10, 8, 10, 8) };
    private readonly Button _area = new() { Content = "منحنى" }, _bars = new() { Content = "أعمدة" };
    private StatisticsAnalytics? _data;
    private double?[] _previousValues = Array.Empty<double?>();
    private bool _columns;
    private bool _renderPending;
    private bool _animatePending;
    private readonly Border _chartCard;

    public StatisticsDashboard()
    {
        FlowDirection = FlowDirection.RightToLeft;
        var root = new StackPanel(); Content = root;
        string[] labels = { "الإجمالي", "مقارنة بالفترة السابقة", "المتوسط اليومي", "أعلى يوم" };
        for (int i = 0; i < 4; i++)
        {
            var body = new StackPanel(); _labels[i] = Text(labels[i], 12); body.Children.Add(_labels[i]);
            _values[i] = Text("—", i == 1 ? 24 : 26, true); _values[i].Margin = new Thickness(0, 10, 0, 6);
            _values[i].TextWrapping = TextWrapping.NoWrap; _values[i].TextTrimming = TextTrimming.CharacterEllipsis;
            body.Children.Add(_values[i]); _hints[i] = Text("", 11); body.Children.Add(_hints[i]);
            if (i == 0) { _sparkline.Margin = new Thickness(0, 5, 0, 0); body.Children.Add(_sparkline); }
            var card = Card(body, 16); card.Margin = new Thickness(0, 0, 10, 10); card.MinHeight = 143; _cards.Children.Add(card);
        }
        root.Children.Add(_cards);
        var chart = new StackPanel();
        var headings = new StackPanel(); headings.Children.Add(_title); _subtitle.Margin = new Thickness(0, 6, 0, 12); headings.Children.Add(_subtitle);
        chart.Children.Add(headings);
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var button in new[] { _area, _bars })
        {
            button.Padding = new Thickness(15, 7, 15, 7); button.Margin = new Thickness(0, 0, 6, 0);
            button.Cursor = System.Windows.Input.Cursors.Hand;
            button.SetResourceReference(BackgroundProperty, "AppInputBg"); button.SetResourceReference(ForegroundProperty, "AppTextPrimary");
            button.SetResourceReference(BorderBrushProperty, "AppBorder"); toolbar.Children.Add(button);
            StyleButton(button);
        }
        foreach (var check in new[] { _compare, _average })
        {
            check.SetResourceReference(ForegroundProperty, "AppTextSecondary");
            StyleCheck(check);
            check.VerticalContentAlignment = VerticalAlignment.Center; toolbar.Children.Add(check);
            check.Checked += (_, _) => QueueRender(false); check.Unchecked += (_, _) => QueueRender(false);
        }
        _area.Click += (_, _) => { _columns = false; QueueRender(true); };
        _bars.Click += (_, _) => { _columns = true; QueueRender(true); };
        chart.Children.Add(toolbar); chart.Children.Add(_plot);
        _note.Margin = new Thickness(0, 12, 0, 0); chart.Children.Add(_note);
        _chartCard = Card(chart, 22); _chartCard.Margin = new Thickness(0, 0, 0, 16); root.Children.Add(_chartCard);
        SizeChanged += (_, _) => { ArrangeCards(); QueueRender(false); };
        Loaded += (_, _) => QueueRender(false);
        Unloaded += (_, _) => ChartInspector.Detach(_plot);
    }

    internal void ShowData(StatisticsAnalytics data)
    {
        IsEnabled = true; Opacity = 1;
        _previousValues = _data?.Title == data.Title ? _data.Buckets.Select(b => b.Value).ToArray() : Array.Empty<double?>();
        _data = data;
        _title.Text = data.Title + " حسب الفترة";
        _labels[0].Text = "إجمالي " + data.Title;
        _subtitle.Text = $"{data.Start:dd/MM/yyyy} – {data.End:dd/MM/yyyy}  ·  {data.Unit}";
        _average.Content = data.Period == StatisticsPeriod.Year ? "المتوسط الشهري" : "المتوسط اليومي";
        string format = data.Money ? "{0:N0} ل.س" : "{0:N0}";
        AnimatedNumber.Show(_values[0], data.Total, format);
        AnimatedNumber.Cancel(_values[1]); _values[1].Text = data.ElapsedDays == 0 ? "—" : StatisticsAnalytics.Change(data.Total, data.PreviousTotal);
        _values[1].FontSize = data.PreviousTotal == 0 && data.Total > 0 ? 14 : 24;
        double delta = data.Total - data.PreviousTotal;
        _values[1].Foreground = data.ElapsedDays == 0 || delta == 0 || data.PreviousTotal == 0 ? Resource("AppTextPrimary")
            : Brush(delta > 0 == !data.LowerIsBetter ? "#0D9488" : "#E87951");
        AnimatedNumber.Show(_values[2], data.Average, format);
        AnimatedNumber.Show(_values[3], data.Peak, format);
        _values[0].ToolTip = data.Format(data.Total); _values[2].ToolTip = data.Format(data.Average); _values[3].ToolTip = data.Format(data.Peak);
        _values[1].ToolTip = $"{data.Format(data.Total)} / {data.Format(data.PreviousTotal)}";
        _hints[0].Text = data.Money ? "المبالغ المسجلة خلال الفترة" : "العدد المسجل خلال الفترة";
        _hints[1].Text = data.ElapsedDays == 0 ? "الفترة لم تبدأ بعد" : $"السابق: {data.Format(data.PreviousTotal)}";
        _hints[2].Text = $"على {data.ElapsedDays:N0} يوم تقويمي منقضٍ";
        _hints[3].Text = data.PeakDate?.ToString("dddd، dd/MM/yyyy", CultureInfo.GetCultureInfo("ar-SY")) ?? "لا توجد قيمة موجبة";
        _note.Text = data.ElapsedDays == 0 ? "الفترة لم تبدأ بعد؛ لا يتم عرض الأيام القادمة كقيم صفرية."
            : $"المقارنة: {data.PreviousStart:dd/MM/yyyy} – {data.PreviousEnd:dd/MM/yyyy} · الأيام القادمة فارغة · المتوسط يشمل الأيام المنقضية دون نشاط";
        ArrangeCards(); QueueRender(true);
    }

    internal void SetLoading()
    {
        IsEnabled = false; Opacity = .6;
        _note.Text = "جارٍ تحميل الإحصائيات...";
    }

    internal void ShowError()
    {
        _data = null; IsEnabled = true; Opacity = 1;
        ChartInspector.Detach(_plot); _plot.Children.Clear(); _sparkline.Children.Clear();
        _title.Text = "تعذر تحميل الإحصائيات"; _subtitle.Text = "";
        foreach (var value in _values) { AnimatedNumber.Cancel(value); value.Text = "—"; }
        foreach (var hint in _hints) hint.Text = "";
        _note.Text = "يرجى إعادة المحاولة باختيار الفترة مرة أخرى.";
    }

    private void ArrangeCards()
    {
        int columns = ActualWidth >= 1000 ? 4 : 2;
        _cards.ColumnDefinitions.Clear(); _cards.RowDefinitions.Clear();
        for (int i = 0; i < columns; i++) _cards.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 4 / columns; i++) _cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < 4; i++) { Grid.SetColumn(_cards.Children[i], i % columns); Grid.SetRow(_cards.Children[i], i / columns); }
    }
    private void QueueRender(bool animate)
    {
        _animatePending |= animate;
        if (_renderPending) return;
        _renderPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            _renderPending = false; bool run = _animatePending; _animatePending = false;
            if (_data != null && _plot.ActualWidth > 160) Draw(run);
        }));
    }

    private void Draw(bool animate)
    {
        if (_data == null) return;
        var data = _data;
        ChartInspector.Detach(_plot); _plot.Children.Clear();
        double width = _plot.ActualWidth, left = 84, right = width - 24, top = 32, baseline = 284;
        double plotHeight = baseline - top, plotWidth = right - left, slot = plotWidth / data.Buckets.Count;
        var accent = Brush(IsDark() ? "#2DD4BF" : "#2563EB");
        _area.FontWeight = !_columns ? FontWeights.Bold : FontWeights.Normal;
        _bars.FontWeight = _columns ? FontWeights.Bold : FontWeights.Normal;
        _area.BorderBrush = !_columns ? accent : Resource("AppBorder"); _bars.BorderBrush = _columns ? accent : Resource("AppBorder");
        var available = data.Buckets.Where(b => b.Value.HasValue).ToList();
        double maxValue = available.Select(b => Math.Max(b.Value!.Value, _compare.IsChecked == true ? b.PreviousValue ?? 0 : 0)).DefaultIfEmpty(0).Max();
        double minValue = available.Select(b => Math.Min(b.Value!.Value, _compare.IsChecked == true ? b.PreviousValue ?? 0 : 0)).DefaultIfEmpty(0).Min();
        double step = NiceStep(Math.Max(1, maxValue - minValue) / 4, !data.Money);
        double min = Math.Floor(Math.Min(0, minValue) / step) * step;
        double max = Math.Max(step, Math.Ceiling(maxValue / step) * step);
        double Y(double value) => top + (max - value) / (max - min) * plotHeight;
        double zero = Y(0);
        for (double value = min; value <= max + step * .01; value += step)
        {
            double y = Y(value);
            _plot.Children.Add(new Line { X1 = left, X2 = right, Y1 = y, Y2 = y, Stroke = Resource("AppBorder"), Opacity = value == 0 ? .9 : .5, StrokeThickness = 1 });
            Label(_plot, Compact(value), 0, y - 9, left - 12, 12, TextAlignment.Right);
        }
        Label(_plot, data.Unit, 0, 4, left - 12, 11, TextAlignment.Right);
        int stride = Math.Max(1, (int)Math.Ceiling(data.Buckets.Count / Math.Max(2, plotWidth / 66)));
        for (int i = 0; i < data.Buckets.Count; i++)
        {
            if (i % stride != 0 && i != data.Buckets.Count - 1) continue;
            // Avoid squeezing the final two labels into one slot.
            if (i != data.Buckets.Count - 1 && i + stride >= data.Buckets.Count && data.Buckets.Count - 1 - i < stride) continue;
            string label = data.Period == StatisticsPeriod.Year ? data.Buckets[i].Date.ToString("MMM", CultureInfo.GetCultureInfo("ar-SY")) : data.Buckets[i].Date.ToString("dd/MM");
            Label(_plot, label, left + (i + .5) * slot - 31, baseline + 12, 62, 12);
        }
        if (available.Count == 0 || maxValue == 0 && minValue == 0)
            Label(_plot, available.Count == 0 ? "الفترة لم تبدأ بعد" : "لا توجد بيانات لهذه الفترة", left, top + 90, plotWidth, 16);

        if (_average.IsChecked == true && available.Count > 0)
        {
            // Yearly buckets are months; their reference is the mean of elapsed monthly buckets.
            double average = available.Average(b => b.Value!.Value);
            double y = Y(average);
            _plot.Children.Add(new Line { X1 = left, X2 = right, Y1 = y, Y2 = y, Stroke = Brush("#B68B36"), StrokeDashArray = new DoubleCollection { 3, 5 }, Opacity = .8 });
            Label(_plot, (data.Period == StatisticsPeriod.Year ? "المتوسط الشهري " : "المتوسط اليومي ") + Compact(average), right - 180, Math.Max(3, y - 23), 175, 11, TextAlignment.Right);
        }
        var positions = data.Buckets.Select((b, i) => new Point(left + (i + .5) * slot, Y(b.Value ?? 0))).ToList();
        if (_compare.IsChecked == true)
        {
            var runs = new List<Point>();
            for (int i = 0; i <= data.Buckets.Count; i++)
            {
                if (i < data.Buckets.Count && data.Buckets[i].PreviousValue is double previous)
                    runs.Add(new Point(positions[i].X, Y(previous)));
                else if (runs.Count > 0) { DrawReference(runs); runs.Clear(); }
            }
        }
        void DrawReference(List<Point> points)
        {
            if (points.Count == 1) { var dot = new Ellipse { Width = 7, Height = 7, Fill = Resource("AppTextSecondary"), Opacity = .6 }; Place(_plot, dot, points[0].X - 3.5, points[0].Y - 3.5); return; }
            _plot.Children.Add(new Polyline { Points = new PointCollection(points), Stroke = Resource("AppTextSecondary"), StrokeThickness = 1.8, StrokeDashArray = new DoubleCollection { 5, 5 }, Opacity = .6, IsHitTestVisible = false });
        }
        var emphasis = new List<FrameworkElement>();
        if (_columns)
        {
            double barWidth = Math.Min(54, slot * .55);
            for (int i = 0; i < data.Buckets.Count; i++)
            {
                if (data.Buckets[i].Value is not double value || value == 0) continue;
                double y = Math.Min(zero, positions[i].Y), h = Math.Abs(zero - positions[i].Y);
                var bar = new Rectangle { Width = barWidth, Height = Math.Max(1, h), RadiusX = 4, RadiusY = 4, Fill = accent, Tag = i };
                Place(_plot, bar, positions[i].X - barWidth / 2, y); emphasis.Add(bar);
                if (animate)
                {
                    double oldHeight = i < _previousValues.Length ? Math.Abs(Y(_previousValues[i] ?? 0) - zero) : 0;
                    Animate(bar, HeightProperty, oldHeight, bar.Height, 380, Math.Min(i * 10, 140));
                    double oldTop = i < _previousValues.Length ? Math.Min(zero, Y(_previousValues[i] ?? 0)) : zero;
                    Animate(bar, Canvas.TopProperty, oldTop, y, 380, Math.Min(i * 10, 140));
                }
            }
        }
        else if (available.Count > 0)
        {
            var actual = positions.Take(available.Count).ToList();
            var lineFigure = Figure(actual, false, zero, animate);
            var fillFigure = Figure(actual, true, zero, animate);
            Color color = accent.Color;
            var fill = new System.Windows.Shapes.Path
            {
                Data = new PathGeometry(new[] { fillFigure }),
                Fill = new LinearGradientBrush(Color.FromArgb(65, color.R, color.G, color.B), Color.FromArgb(3, color.R, color.G, color.B), 90),
                IsHitTestVisible = false
            };
            _plot.Children.Add(fill);
            var line = new System.Windows.Shapes.Path { Data = new PathGeometry(new[] { lineFigure }), Stroke = accent, StrokeThickness = 2.8, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
            _plot.Children.Add(line);
            if (animate && _previousValues.Length == 0) { ChartMotion.RevealLine(line, width, _plot.Height); ChartMotion.FadeIn(fill, 100); }
            for (int i = 0; i < actual.Count; i++)
            {
                var marker = new Ellipse { Width = 7, Height = 7, Fill = Resource("AppCardBg"), Stroke = accent, StrokeThickness = 2, IsHitTestVisible = false, Tag = i };
                Place(_plot, marker, actual[i].X - 3.5, actual[i].Y - 3.5); emphasis.Add(marker);
                if (animate && i < _previousValues.Length) Animate(marker, Canvas.TopProperty, Y(_previousValues[i] ?? 0) - 3.5, actual[i].Y - 3.5, 380);
                else if (animate) ChartMotion.FadeIn(marker, 150 + i * 8);
            }
            PathFigure Figure(List<Point> coords, bool closed, double baseY, bool morph)
            {
                var figure = new PathFigure { StartPoint = closed ? new Point(coords[0].X, baseY) : coords[0], IsClosed = closed };
                for (int i = closed ? 0 : 1; i < coords.Count; i++)
                {
                    var segment = new LineSegment(coords[i], true); figure.Segments.Add(segment);
                    if (morph && i < _previousValues.Length && MotionPreferences.Enabled)
                        segment.BeginAnimation(LineSegment.PointProperty, new PointAnimation(new Point(coords[i].X, Y(_previousValues[i] ?? 0)), coords[i], MotionPreferences.Duration(380)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
                }
                if (closed) figure.Segments.Add(new LineSegment(new Point(coords[^1].X, baseY), true));
                else if (morph && _previousValues.Length > 0 && MotionPreferences.Enabled)
                    figure.BeginAnimation(PathFigure.StartPointProperty, new PointAnimation(new Point(coords[0].X, Y(_previousValues[0] ?? 0)), coords[0], MotionPreferences.Duration(380)) { FillBehavior = FillBehavior.Stop });
                return figure;
            }
        }
        if (available.Count > 0 && maxValue > 0)
        {
            int peakIndex = Enumerable.Range(0, data.Buckets.Count).Where(i => data.Buckets[i].Value.HasValue).MaxBy(i => data.Buckets[i].Value);
            Label(_plot, Compact(data.Buckets[peakIndex].Value!.Value), Math.Clamp(positions[peakIndex].X - 45, left, right - 90), Math.Max(4, positions[peakIndex].Y - 25), 90, 12);
        }
        var inspection = data.Buckets.Select((b, i) => (b, i)).Where(x => x.b.Value.HasValue)
            .Select(x => new ChartDatum(x.b.Date.ToString(data.Period == StatisticsPeriod.Year ? "MMMM yyyy" : "dddd، dd/MM/yyyy", CultureInfo.GetCultureInfo("ar-SY")),
                x.b.Value!.Value, positions[x.i], _compare.IsChecked == true ? x.b.PreviousValue : null,
                x.b.PreviousDate?.ToString("dd/MM/yyyy"), data.Money ? "ل.س" : "", data.Title)).ToList();
        ChartInspector.Attach(_plot, inspection, top, baseline, left, right, emphasis);
        DrawSparkline(accent);
    }

    private void DrawSparkline(Brush accent)
    {
        _sparkline.Children.Clear(); if (_data == null || _data.DailyValues.Count == 0) return;
        double width = Math.Max(40, (_cards.ActualWidth / (ActualWidth >= 1000 ? 4 : 2)) - 44);
        double min = Math.Min(0, _data.DailyValues.Min()), max = Math.Max(1, _data.DailyValues.Max());
        var points = _data.DailyValues.Select((v, i) => new Point(i * width / Math.Max(1, _data.DailyValues.Count - 1), 25 - (v - min) / (max - min) * 22));
        _sparkline.Children.Add(new Polyline { Points = new PointCollection(points), Stroke = accent, StrokeThickness = 1.6, Opacity = .8 });
    }
    internal static double NiceStep(double raw, bool whole)
    {
        double power = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(.000001, raw))));
        double n = raw / power, step = (n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10) * power;
        return whole ? Math.Max(1, Math.Ceiling(step)) : step;
    }
    private static string Compact(double value) => Math.Abs(value) >= 1_000_000 ? $"{value / 1_000_000:0.#}M" : Math.Abs(value) >= 1000 ? $"{value / 1000:0.#}K" : $"{value:0.#}";
    private static void Animate(FrameworkElement element, DependencyProperty property, double from, double to, int ms, int delay = 0)
    {
        if (!MotionPreferences.Enabled) return;
        // Hold the start value during the stagger; BeginTime alone briefly exposes the final bar.
        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if (delay > 0) animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(MotionPreferences.Duration(delay))));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(MotionPreferences.Duration(delay + ms)), new KeySpline(.16, 1, .3, 1)));
        element.BeginAnimation(property, animation);
    }
    internal static TextBlock Text(string value, double size = 13, bool bold = false)
    {
        var text = new TextBlock { Text = value, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.RightToLeft };
        text.SetResourceReference(TextBlock.ForegroundProperty, bold ? "AppTextPrimary" : "AppTextSecondary"); return text;
    }
    internal static void StyleButton(Button button)
    {
        button.FontSize = 12; button.BorderThickness = new Thickness(1);
        var border = new FrameworkElementFactory(typeof(Border)); border.Name = "Surface";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(OpacityProperty, .76, "Surface")); template.Triggers.Add(hover);
        button.Template = template;
    }
    private static void StyleCheck(CheckBox check)
    {
        check.FontSize = 12; check.Cursor = System.Windows.Input.Cursors.Hand;
        check.Margin = new Thickness(6, 0, 0, 0);
        var border = new FrameworkElementFactory(typeof(Border)); border.Name = "Surface";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8)); border.SetValue(Border.PaddingProperty, new Thickness(12, 8, 12, 8));
        border.SetResourceReference(Border.BackgroundProperty, "AppInputBg"); border.SetResourceReference(Border.BorderBrushProperty, "AppBorder"); border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        var stack = new FrameworkElementFactory(typeof(StackPanel)); stack.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var dot = new FrameworkElementFactory(typeof(Ellipse)); dot.Name = "Dot"; dot.SetValue(WidthProperty, 6d); dot.SetValue(HeightProperty, 6d);
        dot.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); dot.SetResourceReference(Shape.FillProperty, "AppTextSecondary"); dot.SetValue(OpacityProperty, .3d); stack.AppendChild(dot);
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(MarginProperty, new Thickness(7, 0, 0, 0)); stack.AppendChild(content); border.AppendChild(stack);
        var template = new ControlTemplate(typeof(CheckBox)) { VisualTree = border };
        var selected = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true }; selected.Setters.Add(new Setter(OpacityProperty, 1d, "Dot"));
        selected.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("AppTextSecondary"), "Surface")); template.Triggers.Add(selected);
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(OpacityProperty, .76, "Surface")); template.Triggers.Add(hover);
        check.Template = template;
    }
    internal static Border Card(UIElement child, double padding) { var card = new Border { Child = child, CornerRadius = new CornerRadius(16), Padding = new Thickness(padding), BorderThickness = new Thickness(1) }; card.SetResourceReference(Border.BackgroundProperty, "AppCardBg"); card.SetResourceReference(Border.BorderBrushProperty, "AppBorder"); return card; }
    internal static Brush Resource(string name) => Application.Current.TryFindResource(name) as Brush ?? Brushes.Gray;
    internal static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    internal static bool IsDark() => Resource("AppTextPrimary") is SolidColorBrush brush && brush.Color.R > 180;
    internal static void Place(Canvas canvas, FrameworkElement element, double x, double y) { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); canvas.Children.Add(element); }
    private static void Label(Canvas canvas, string value, double x, double y, double width, double size, TextAlignment alignment = TextAlignment.Center)
    {
        var text = Text(value, size); text.Width = Math.Max(1, width); text.TextAlignment = alignment; text.IsHitTestVisible = false; text.TextWrapping = TextWrapping.NoWrap; Place(canvas, text, x, y);
    }
}
