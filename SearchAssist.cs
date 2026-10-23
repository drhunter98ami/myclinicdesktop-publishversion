using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MyClinic;

public static class SearchAssist
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached("Text", typeof(string), typeof(SearchAssist), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached("Kind", typeof(string), typeof(SearchAssist), new PropertyMetadata("Name", Changed));
    public static readonly DependencyProperty NameQueryProperty = DependencyProperty.RegisterAttached("NameQuery", typeof(string), typeof(SearchAssist), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.Inherits, Changed));
    public static readonly DependencyProperty PhoneQueryProperty = DependencyProperty.RegisterAttached("PhoneQuery", typeof(string), typeof(SearchAssist), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.Inherits, Changed));
    private static readonly DependencyProperty SummaryProperty = DependencyProperty.RegisterAttached("Summary", typeof(WrapPanel), typeof(SearchAssist));
    public static string GetText(DependencyObject target) => (string)target.GetValue(TextProperty);
    public static void SetText(DependencyObject target, string value) => target.SetValue(TextProperty, value);
    public static string GetKind(DependencyObject target) => (string)target.GetValue(KindProperty);
    public static void SetKind(DependencyObject target, string value) => target.SetValue(KindProperty, value);
    public static string GetNameQuery(DependencyObject target) => (string)target.GetValue(NameQueryProperty);
    public static void SetNameQuery(DependencyObject target, string value) => target.SetValue(NameQueryProperty, value);
    public static string GetPhoneQuery(DependencyObject target) => (string)target.GetValue(PhoneQueryProperty);
    public static void SetPhoneQuery(DependencyObject target, string value) => target.SetValue(PhoneQueryProperty, value);

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not TextBlock block) return;
        // DataTemplate bindings are template values, so ReadLocalValue reports UnsetValue
        // even when they supply text. Inspect the effective value source instead.
        if (e.Property != TextProperty &&
            DependencyPropertyHelper.GetValueSource(block, TextProperty).BaseValueSource == BaseValueSource.Default) return;
        string text = GetText(block) ?? "";
        string query = (GetKind(block) == "Phone" ? GetPhoneQuery(block) : GetNameQuery(block))?.Trim() ?? "";
        block.Inlines.Clear();
        int offset = 0;
        while (query.Length > 0 && offset < text.Length)
        {
            int match = text.IndexOf(query, offset, StringComparison.CurrentCultureIgnoreCase);
            if (match < 0) break;
            if (match > offset) block.Inlines.Add(new Run(text[offset..match]));
            var highlight = new Run(text.Substring(match, query.Length))
            {
                Background = new SolidColorBrush(Color.FromArgb(110, 245, 158, 11)), FontWeight = FontWeights.Bold
            };
            block.Inlines.Add(highlight); offset = match + query.Length;
        }
        if (offset < text.Length) block.Inlines.Add(new Run(text[offset..]));
        System.Windows.Automation.AutomationProperties.SetName(block, text);
    }

    public static void Summary(ItemsControl list, int count, params (string Label, TextBox Input)[] filters)
    {
        if (list.GetValue(SummaryProperty) is not WrapPanel summary)
        {
            if (list.Parent is not Grid grid) return;
            foreach (UIElement child in grid.Children)
            {
                Grid.SetRow(child, Grid.GetRow(child) + 1);
                if (child is Border { Name: "LoadingOverlay" }) Grid.SetRowSpan(child, 2);
            }
            if (grid.RowDefinitions.Count == 0) grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Insert(0, new RowDefinition { Height = GridLength.Auto });
            summary = new WrapPanel { Margin = new Thickness(4, 0, 4, 10), FlowDirection = FlowDirection.RightToLeft };
            grid.Children.Add(summary); list.SetValue(SummaryProperty, summary);
        }
        summary.Children.Clear();
        var result = new TextBlock { Text = $"{count:N0} نتيجة", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 5, 12, 5), FontWeight = FontWeights.SemiBold };
        result.SetResourceReference(TextBlock.ForegroundProperty, "AppTextSecondary"); summary.Children.Add(result);
        foreach (var (label, input) in filters)
        {
            string query = input.Text.Trim();
            if (query.Length == 0) continue;
            var chip = new Button { Content = $"{label}: {query}  ×", MaxWidth = 320, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 2, 6, 2), Cursor = System.Windows.Input.Cursors.Hand };
            chip.SetResourceReference(Control.BackgroundProperty, "AppInputBg");
            chip.SetResourceReference(Control.ForegroundProperty, "AppTextPrimary");
            chip.SetResourceReference(Control.BorderBrushProperty, "AppBorder");
            System.Windows.Automation.AutomationProperties.SetName(chip, "إزالة تصفية " + label + ": " + query);
            chip.Click += (_, _) => { input.Clear(); input.Focus(); };
            summary.Children.Add(chip);
        }
    }
}
