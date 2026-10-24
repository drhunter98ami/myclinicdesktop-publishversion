using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MyClinic;

public static class WorkspaceUi
{
    public static void Configure(FrameworkElement view)
    {
        bool dark = (view.TryFindResource("AppTextPrimary") as SolidColorBrush)?.Color.R > 180;
        bool original = Window.GetWindow(view) is OriginalTheme.MainWindow;
        for (FrameworkElement? ancestor = view; ancestor != null; ancestor = ancestor.Parent as FrameworkElement)
            original |= ancestor.GetType().Namespace == "MyClinic.OriginalTheme";
        if (original)
        {
            view.Resources["AppSidebarHoverBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#243449" : "#E5F1EF"));
            view.Resources["AppTextSecondary"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#B4C2D4" : "#52627A"));
        }
        view.Resources["WorkspaceAccent"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#0F8B79" : "#087565"));
        view.Resources["WorkspaceWarning"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#FCD34D" : "#8A5800"));
        view.Resources["WorkspaceDanger"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#FCA5A5" : "#A52632"));
    }
    public static string Normalize(string? value) => new((value ?? "").Trim().Select(c => c is >= '٠' and <= '٩' ? (char)('0' + c - '٠') : c is >= '۰' and <= '۹' ? (char)('0' + c - '۰') : c == '٫' ? '.' : c == '٬' ? ',' : c).ToArray());
    public static bool Number(string text, out decimal value) => decimal.TryParse(Normalize(text), NumberStyles.Number, CultureInfo.CurrentCulture, out value) || decimal.TryParse(Normalize(text), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    public static string Money(decimal value, string currency) => "\u202A" + $"{value:N2} {currency}" + "\u202C";
    public static decimal Rate(AppDbContext db) => db.AppSettings.Select(s => s.UsdToSypRate).FirstOrDefault() is > 0 and var rate ? rate : 15000;
    public static void Reveal(FrameworkElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        var transform = new TranslateTransform(); element.RenderTransform = transform;
        if (!MotionPreferences.Enabled) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.4, 1, MotionPreferences.Duration(180)) { FillBehavior = FillBehavior.Stop });
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(MotionPreferences.Distance(8), 0, MotionPreferences.Duration(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
    public static void OpenExpense(FrameworkElement owner, int id)
    {
        if (Window.GetWindow(owner) is MainWindow modern) modern.OpenExpense(id);
        else if (Window.GetWindow(owner) is OriginalTheme.MainWindow original) original.OpenExpense(id);
    }
}

/// <summary>Owned modal with native keyboard focus containment and inline validation.</summary>
public sealed class ClinicDialog : Window
{
    public StackPanel Fields { get; } = new();
    public TextBlock Error { get; } = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 6) };
    public Button Submit { get; }
    public Button CancelButton { get; }
    public bool Busy { get; private set; }
    public ClinicDialog(FrameworkElement owner, string title, string submit = "حفظ")
    {
        Owner = Window.GetWindow(owner); Title = title; FlowDirection = FlowDirection.RightToLeft;
        FontFamily = new("Segoe UI"); FontSize = 14; Width = Math.Min(520, SystemParameters.WorkArea.Width - 40);
        SizeToContent = SizeToContent.Height; MaxHeight = SystemParameters.WorkArea.Height - 60;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/MyClinic;component/WorkspaceStyles.xaml", UriKind.Relative) });
        foreach (string key in new[] { "AppCardBg", "AppInputBg", "AppTextPrimary", "AppTextSecondary", "AppBorder", "AppSidebarHoverBg", "WorkspaceAccent", "WorkspaceDanger" })
            if (owner.TryFindResource(key) is object value) Resources[key] = value;
        SetResourceReference(BackgroundProperty, "AppCardBg"); SetResourceReference(ForegroundProperty, "AppTextPrimary");
        var checkBoxStyle = new Style(typeof(CheckBox));
        checkBoxStyle.Setters.Add(new Setter(Control.ForegroundProperty, FindResource("AppTextPrimary")));
        Resources[typeof(CheckBox)] = checkBoxStyle;
        InteractionMotion.SetEnabled(this, true);
        Error.SetResourceReference(TextBlock.ForegroundProperty, "WorkspaceDanger");
        var layout = new StackPanel { Margin = new(22) };
        layout.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("WorkspaceHeading"), Margin = new(0, 0, 0, 18) });
        layout.Children.Add(Fields); layout.Children.Add(Error);
        var actions = new WrapPanel { Margin = new(0, 10, 0, 0) };
        Submit = new Button { Content = submit, Style = (Style)FindResource("WorkspacePrimary"), MinWidth = 115 };
        var cancel = new Button { Content = "إلغاء", Style = (Style)FindResource("WorkspaceButton"), Margin = new(8, 0, 0, 0), IsCancel = true };
        CancelButton = cancel;
        actions.Children.Add(Submit); actions.Children.Add(cancel); layout.Children.Add(actions);
        Content = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !Busy) { Close(); e.Handled = true; } };
        Closing += (_, e) => { if (Busy) e.Cancel = true; };
        Loaded += (_, _) => { WorkspaceUi.Reveal(layout); MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); };
    }
    public void Note(string text) => Fields.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("WorkspaceHint"), Margin = new(0, 0, 0, 12) });
    public T Field<T>(string label, T control) where T : FrameworkElement
    {
        Fields.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("WorkspaceLabel") });
        control.Margin = new(0, 0, 0, 12); Fields.Children.Add(control); return control;
    }
    public TextBox Input(string label, string value = "", bool numeric = false) => Field(label, new TextBox { Text = value, Style = (Style)FindResource("WorkspaceInput"), FlowDirection = numeric ? FlowDirection.LeftToRight : FlowDirection.RightToLeft });
    public ComboBox Choice(string label, IEnumerable<string> items, string? selected = null)
    {
        var box = Field(label, new ComboBox { ItemsSource = items.ToList(), Style = (Style)FindResource("WorkspaceCombo") });
        box.SelectedItem = selected; if (box.SelectedIndex < 0) box.SelectedIndex = 0; return box;
    }
    public DatePicker Date(string label, DateTime? value = null) => Field(label, new DatePicker { SelectedDate = value, MinHeight = 38, Style = (Style)FindResource("WorkspaceDate") });
    public void OnSubmit(Func<Task> action) => Submit.Click += async (_, _) =>
    {
        if (Busy) return;
        try { Error.Text = ""; Busy = true; Submit.IsEnabled = false; Fields.IsEnabled = false; await action(); }
        catch (Exception ex) { Error.Text = ex.Message; }
        finally { Busy = false; Submit.IsEnabled = true; Fields.IsEnabled = true; }
    };
    public void Complete() { Busy = false; DialogResult = true; }
}
