using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MyClinic;

/// <summary>FDI-specific schematic labial/buccal silhouettes; never used to infer canal anatomy.</summary>
public sealed class ToothGlyph : FrameworkElement
{
    public static readonly DependencyProperty ToothIdProperty = DependencyProperty.Register(nameof(ToothId), typeof(string), typeof(ToothGlyph), new FrameworkPropertyMetadata("11", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(ToothGlyph), new PropertyMetadata(false, SelectionChanged));
    public static readonly DependencyProperty IsHighlightedProperty = DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(ToothGlyph), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SelectionProgressProperty = DependencyProperty.Register(nameof(SelectionProgress), typeof(double), typeof(ToothGlyph), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public string ToothId { get => (string)GetValue(ToothIdProperty); set => SetValue(ToothIdProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty); set => SetValue(IsHighlightedProperty, value); }
    public double SelectionProgress { get => (double)GetValue(SelectionProgressProperty); set => SetValue(SelectionProgressProperty, value); }

    private static void SelectionChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        var glyph = (ToothGlyph)target;
        double from = glyph.SelectionProgress, to = (bool)e.NewValue ? 1 : 0;
        glyph.BeginAnimation(SelectionProgressProperty, null);
        glyph.SelectionProgress = to;
        if (glyph.IsLoaded && MotionPreferences.Enabled)
            glyph.BeginAnimation(SelectionProgressProperty, new DoubleAnimation(from, to, MotionPreferences.Duration(160)) { FillBehavior = FillBehavior.Stop });
    }

    public static string ToothKind(int id)
    {
        int n = id % 10;
        return n <= 2 ? (n == 1 ? "قاطع مركزي" : "قاطع جانبي") : n == 3 ? "ناب" :
            id / 10 >= 5 ? (n == 4 ? "ضرس لبني أول" : "ضرس لبني ثانٍ") : n <= 5 ? (n == 4 ? "ضاحك أول" : "ضاحك ثانٍ") :
            n == 6 ? "ضرس أول" : n == 7 ? "ضرس ثانٍ" : "ضرس العقل";
    }
    public static string Description(string id)
    {
        if (!int.TryParse(id, out int tooth)) return id;
        bool upper = tooth / 10 is 1 or 2 or 5 or 6;
        bool right = tooth / 10 is 1 or 4 or 5 or 8;
        return $"{id} · {ToothKind(tooth)} · {(upper ? "علوي" : "سفلي")} {(right ? "أيمن" : "أيسر")}";
    }
    public static bool IsValidId(int id) => id / 10 is >= 1 and <= 8 &&
        id % 10 >= 1 && id % 10 <= (id / 10 <= 4 ? 8 : 5);

    public static Geometry CrownGeometry(int id)
    {
        int n = id % 10; bool primary = id / 10 >= 5, upper = id / 10 is 1 or 2 or 5 or 6;
        double width = n switch { 1 => upper ? 25 : 17, 2 => upper ? 20 : 18, 3 => 23, 4 => primary ? 31 : 24, 5 => primary ? 36 : 25, 6 => upper ? 37 : 40, 7 => 35, _ => 32 };
        if (primary && n <= 3) width *= .88;
        double l = Math.Round(24 - width / 2), r = Math.Round(24 + width / 2);
        string path = n <= 2
            ? $"M {l+2},10 Q 24,8 {r-2},10 Q {r+1},19 {r-2},31 Q 24,38 {l+2},31 Q {l-1},19 {l+2},10 Z"
            : n == 3
            ? $"M {l+1},18 Q {l+5},15 24,6 Q {r-3},13 {r},18 L {r-3},32 Q 24,39 {l+3},32 Z"
            : n <= 5 && !primary
            ? $"M {l},18 Q {l},11 {l+5},12 L 23,7 L 29,13 Q {r+2},10 {r},19 L {r-2},31 Q 24,37 {l+2},31 Z"
            : $"M {l},18 Q {l-2},9 {l+5},11 Q {l+10},6 24,12 Q {r-8},7 {r-4},13 Q {r+3},10 {r},21 L {r-2},32 Q 24,39 {l+2},32 Z";
        return Geometry.Parse(path);
    }

    public static Geometry RootGeometry(int id)
    {
        int n = id % 10; bool primary = id / 10 >= 5, upper = id / 10 is 1 or 2 or 5 or 6;
        bool molar = primary ? n >= 4 : n >= 6;
        string path;
        if (molar && upper)
            path = primary
                ? "M 11,30 Q 8,49 3,76 Q 3,82 7,76 L 21,44 Q 23,64 24,80 Q 26,85 28,77 L 29,44 L 42,76 Q 47,83 45,74 Q 39,47 37,30 Z"
                : n == 8
                ? "M 12,30 L 15,59 Q 17,76 23,78 Q 27,78 29,67 Q 37,67 36,56 L 36,30 Z"
                : "M 9,30 Q 9,49 8,73 Q 9,82 13,73 L 21,47 L 22,77 Q 25,86 28,75 L 29,47 L 36,74 Q 40,81 40,72 L 38,30 Z";
        else if (molar)
            path = primary
                ? "M 10,30 Q 7,48 3,76 Q 4,82 8,75 L 23,44 L 40,76 Q 46,83 45,75 Q 39,46 38,30 Z"
                : n == 8
                ? "M 10,30 L 14,62 Q 16,81 21,76 L 25,48 L 30,74 Q 35,82 37,74 L 39,30 Z"
                : "M 7,30 Q 8,50 8,74 Q 9,83 14,75 L 24,46 L 32,74 Q 38,83 40,74 L 40,30 Z";
        else if (!primary && upper && n == 4)
            path = "M 14,30 L 13,63 Q 13,82 18,75 L 24,49 L 30,75 Q 35,81 35,72 L 34,30 Z";
        else
        {
            double length = n == 3 ? 85 : primary ? 71 : n <= 2 ? 78 : 80;
            double neck = n == 1 && !upper ? 6 : n == 3 ? 9 : 8;
            path = $"M {24-neck},30 Q {24-neck-1},49 21,{length-5} Q 22,{length+3} 26,{length-2} Q 34,53 {24+neck},30 Z";
        }
        return Geometry.Parse(path);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (!int.TryParse(ToothId, out int id) || !IsValidId(id)) return;
        bool upper = id / 10 is 1 or 2 or 5 or 6, mirror = id / 10 is 2 or 3 or 6 or 7;
        dc.PushTransform(new ScaleTransform(ActualWidth / 48, ActualHeight / 92));
        if (upper) dc.PushTransform(new ScaleTransform(1, -1, 24, 46));
        if (mirror) dc.PushTransform(new ScaleTransform(-1, 1, 24, 46));
        Color accent = (TryFindResource("DashActiveFilterBg") as SolidColorBrush)?.Color ?? Color.FromRgb(13, 148, 136);
        Color outline = (TryFindResource("DashCalendarTodayBorder") as SolidColorBrush)?.Color ?? Color.FromRgb(80, 124, 130);
        double p = SelectionProgress;
        Color Blend(Color a, Color b) => Color.FromRgb((byte)(a.R+(b.R-a.R)*p), (byte)(a.G+(b.G-a.G)*p), (byte)(a.B+(b.B-a.B)*p));
        var pen = new Pen(new SolidColorBrush(IsHighlighted ? accent : outline), IsHighlighted ? 1.9 : 1.2) { LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(new SolidColorBrush(Blend(Color.FromRgb(229, 220, 192), accent)), pen, RootGeometry(id));
        var enamel = new LinearGradientBrush(Blend(Colors.White, accent), Blend(Color.FromRgb(223, 233, 232), accent), 25);
        dc.DrawGeometry(enamel, pen, CrownGeometry(id));
        var detail = new Pen(new SolidColorBrush(p > .5 ? Colors.White : Color.FromRgb(151, 171, 172)), .75);
        int n = id % 10;
        if (n >= 4)
        {
            dc.DrawGeometry(null, detail, Geometry.Parse("M 16,20 Q 20,17 24,22 Q 28,18 33,20 M 24,22 L 24,29"));
            dc.DrawGeometry(null, detail, Geometry.Parse("M 16,32 Q 24,35 32,32"));
        }
        else dc.DrawGeometry(null, detail, Geometry.Parse(n == 3 ? "M 24,12 Q 22,20 24,29" : "M 18,14 Q 24,16 29,14 M 19,19 L 20,28"));
        if (mirror) dc.Pop(); if (upper) dc.Pop(); dc.Pop();
    }
}

public sealed class ToothDescriptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => ToothGlyph.Description(value?.ToString() ?? "");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
