using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

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

/// <summary>App-themed MessageBox replacement matching the active light/dark and modern/original theme.</summary>
public static class AppMessageBox
{
    public static MessageBoxResult Show(string messageBoxText) =>
        ShowCore(null, messageBoxText, "تنبيه", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);

    public static MessageBoxResult Show(string messageBoxText, string caption) =>
        ShowCore(null, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button) =>
        ShowCore(null, messageBoxText, caption, button, MessageBoxImage.None, MessageBoxResult.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) =>
        ShowCore(null, messageBoxText, caption, button, icon, MessageBoxResult.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult) =>
        ShowCore(null, messageBoxText, caption, button, icon, defaultResult);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options) =>
        ShowCore(null, messageBoxText, caption, button, icon, defaultResult);

    public static MessageBoxResult Show(Window? owner, string messageBoxText) =>
        ShowCore(owner, messageBoxText, "تنبيه", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption) =>
        ShowCore(owner, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton button) =>
        ShowCore(owner, messageBoxText, caption, button, MessageBoxImage.None, MessageBoxResult.None);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) =>
        ShowCore(owner, messageBoxText, caption, button, icon, MessageBoxResult.None);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult) =>
        ShowCore(owner, messageBoxText, caption, button, icon, defaultResult);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options) =>
        ShowCore(owner, messageBoxText, caption, button, icon, defaultResult);

    private static MessageBoxResult ShowCore(Window? owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
    {
        Window? resolvedOwner = owner
            ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsLoaded && w.IsVisible)
            ?? (Application.Current?.MainWindow is { IsLoaded: true, IsVisible: true } main ? main : null);

        bool isDark = DetectDarkTheme(resolvedOwner);
        bool isOriginal = resolvedOwner is OriginalTheme.MainWindow
            || (resolvedOwner?.Owner is OriginalTheme.MainWindow)
            || InterfaceThemeSettings.IsOriginalTheme;

        var dialog = new Window
        {
            Title = string.IsNullOrWhiteSpace(caption) ? "My Clinic" : caption,
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Width = Math.Min(480, SystemParameters.WorkArea.Width - 40),
            MinWidth = 340,
            SizeToContent = SizeToContent.Height,
            MaxHeight = SystemParameters.WorkArea.Height - 60,
            WindowStartupLocation = resolvedOwner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = resolvedOwner == null,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent
        };

        if (resolvedOwner != null && !ReferenceEquals(resolvedOwner, dialog))
            dialog.Owner = resolvedOwner;

        dialog.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/MyClinic;component/WorkspaceStyles.xaml", UriKind.Relative)
        });

        ApplyThemeResources(dialog, resolvedOwner, isDark, isOriginal);
        InteractionMotion.SetEnabled(dialog, true);

        var (badgeText, badgeBrush, badgeBg) = GetIconVisuals(icon, isDark, isOriginal);

        var outerBorder = new Border
        {
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1.2),
            Padding = new Thickness(22, 20, 22, 18),
            Margin = new Thickness(14),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 24,
                ShadowDepth = 6,
                Opacity = isDark ? 0.55 : 0.22
            }
        };
        outerBorder.SetResourceReference(Border.BackgroundProperty, "AppCardBg");
        outerBorder.SetResourceReference(Border.BorderBrushProperty, "AppBorder");

        var rootStack = new StackPanel();

        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconBadge = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(11),
            Background = badgeBg,
            BorderBrush = badgeBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = badgeText,
                Foreground = badgeBrush,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(iconBadge, 0);
        headerGrid.Children.Add(iconBadge);

        var titleText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(caption) ? "تنبيه" : caption,
            Style = (Style)dialog.FindResource("WorkspaceHeading"),
            FontSize = 19,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleText, 1);
        headerGrid.Children.Add(titleText);

        MessageBoxResult result = defaultResult != MessageBoxResult.None
            ? defaultResult
            : button switch
            {
                MessageBoxButton.OK => MessageBoxResult.OK,
                MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
                MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
                MessageBoxButton.YesNo => MessageBoxResult.No,
                _ => MessageBoxResult.None
            };

        var closeBtn = new Button
        {
            Content = "×",
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Style = (Style)dialog.FindResource("WorkspaceButton"),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "إغلاق"
        };
        closeBtn.Click += (_, _) => dialog.Close();
        Grid.SetColumn(closeBtn, 2);
        headerGrid.Children.Add(closeBtn);

        headerGrid.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                dialog.DragMove();
        };

        rootStack.Children.Add(headerGrid);

        var bodyBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12, 14, 12),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 18)
        };
        bodyBorder.SetResourceReference(Border.BackgroundProperty, "AppInputBg");
        bodyBorder.SetResourceReference(Border.BorderBrushProperty, "AppBorder");

        var messageText = new TextBlock
        {
            Text = messageBoxText,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14.5,
            LineHeight = 22
        };
        messageText.SetResourceReference(TextBlock.ForegroundProperty, "AppTextPrimary");
        bodyBorder.Child = new ScrollViewer
        {
            Content = messageText,
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        rootStack.Children.Add(bodyBorder);

        var actionsPanel = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left
        };

        Button CreateActionButton(string label, MessageBoxResult btnResult, bool isPrimary, bool isDanger = false, bool isCancel = false)
        {
            var btn = new Button
            {
                Content = label,
                MinWidth = 96,
                MinHeight = 38,
                Margin = new Thickness(0, 0, 8, 0),
                Style = (Style)dialog.FindResource(isPrimary ? "WorkspacePrimary" : "WorkspaceButton"),
                IsDefault = isPrimary,
                IsCancel = isCancel
            };
            if (isDanger && isPrimary)
            {
                btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isDark ? "#DC2626" : "#B91C1C"));
                btn.BorderBrush = btn.Background;
                btn.Foreground = Brushes.White;
            }
            btn.Click += (_, _) =>
            {
                result = btnResult;
                dialog.DialogResult = true;
            };
            return btn;
        }

        Button? defaultFocusButton = null;
        bool isErrorOrWarn = icon is MessageBoxImage.Error or MessageBoxImage.Warning;

        switch (button)
        {
            case MessageBoxButton.OK:
                defaultFocusButton = CreateActionButton("موافق", MessageBoxResult.OK, true, isDanger: icon == MessageBoxImage.Error, isCancel: true);
                actionsPanel.Children.Add(defaultFocusButton);
                break;

            case MessageBoxButton.OKCancel:
                var okBtn = CreateActionButton("موافق", MessageBoxResult.OK, true, isDanger: isErrorOrWarn);
                var cancelOkBtn = CreateActionButton("إلغاء", MessageBoxResult.Cancel, false, isCancel: true);
                actionsPanel.Children.Add(okBtn);
                actionsPanel.Children.Add(cancelOkBtn);
                defaultFocusButton = defaultResult == MessageBoxResult.Cancel ? cancelOkBtn : okBtn;
                break;

            case MessageBoxButton.YesNo:
                var yesBtn = CreateActionButton("نعم", MessageBoxResult.Yes, true);
                var noBtn = CreateActionButton("لا", MessageBoxResult.No, false, isCancel: true);
                actionsPanel.Children.Add(yesBtn);
                actionsPanel.Children.Add(noBtn);
                defaultFocusButton = defaultResult == MessageBoxResult.No ? noBtn : yesBtn;
                break;

            case MessageBoxButton.YesNoCancel:
                var yesBtn2 = CreateActionButton("نعم", MessageBoxResult.Yes, true);
                var noBtn2 = CreateActionButton("لا", MessageBoxResult.No, false);
                var cancelBtn2 = CreateActionButton("إلغاء", MessageBoxResult.Cancel, false, isCancel: true);
                actionsPanel.Children.Add(yesBtn2);
                actionsPanel.Children.Add(noBtn2);
                actionsPanel.Children.Add(cancelBtn2);
                defaultFocusButton = defaultResult == MessageBoxResult.No ? noBtn2 : defaultResult == MessageBoxResult.Cancel ? cancelBtn2 : yesBtn2;
                break;
        }

        rootStack.Children.Add(actionsPanel);
        outerBorder.Child = rootStack;
        dialog.Content = outerBorder;

        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                dialog.Close();
                e.Handled = true;
            }
        };

        dialog.Loaded += (_, _) =>
        {
            WorkspaceUi.Reveal(outerBorder);
            defaultFocusButton?.Focus();
        };

        dialog.ShowDialog();
        return result;
    }

    private static bool DetectDarkTheme(Window? owner)
    {
        if (owner is MainWindow m) return m.IsDarkTheme;
        if (owner is OriginalTheme.MainWindow om) return om.IsDarkTheme;
        if (Application.Current?.MainWindow is MainWindow am) return am.IsDarkTheme;
        if (Application.Current?.MainWindow is OriginalTheme.MainWindow aom) return aom.IsDarkTheme;
        if (owner?.TryFindResource("AppTextPrimary") is SolidColorBrush b) return b.Color.R > 180;
        try
        {
            if (ThemePreference.FollowSystem) return ThemePreference.SystemDark;
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyClinicApp", "ThemeConfig.txt");
            if (File.Exists(path)) return string.Equals(File.ReadAllText(path).Trim(), "Dark", StringComparison.OrdinalIgnoreCase);
        }
        catch { }
        return false;
    }

    private static void ApplyThemeResources(Window dialog, Window? owner, bool isDark, bool isOriginal)
    {
        static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

        foreach (string key in new[] { "AppCardBg", "AppWindowBg", "AppInputBg", "AppTextPrimary", "AppTextSecondary", "AppBorder", "AppSidebarHoverBg", "WorkspaceAccent", "WorkspaceDanger", "WorkspaceWarning" })
        {
            if (owner?.TryFindResource(key) is object val)
                dialog.Resources[key] = val;
            else if (Application.Current?.MainWindow?.TryFindResource(key) is object appVal)
                dialog.Resources[key] = appVal;
        }

        dialog.Resources["AppCardBg"] ??= Brush(isDark ? "#121722" : "#FFFFFF");
        dialog.Resources["AppWindowBg"] ??= Brush(isDark ? "#0B0F17" : "#F3F7F6");
        dialog.Resources["AppInputBg"] ??= Brush(isDark ? "#182030" : "#F8FAFC");
        dialog.Resources["AppTextPrimary"] ??= Brush(isDark ? "#F1F5F9" : "#13284A");
        dialog.Resources["AppTextSecondary"] ??= Brush(isDark ? "#94A3B8" : "#52627A");
        dialog.Resources["AppBorder"] ??= Brush(isDark ? "#263248" : "#DCE5E7");
        dialog.Resources["AppSidebarHoverBg"] ??= Brush(isDark ? "#1E293B" : "#E5F1EF");
        dialog.Resources["WorkspaceAccent"] = Brush(isOriginal ? (isDark ? "#3B82F6" : "#235DF6") : (isDark ? "#0F8B79" : "#087565"));
        dialog.Resources["WorkspaceWarning"] = Brush(isDark ? "#FCD34D" : "#8A5800");
        dialog.Resources["WorkspaceDanger"] = Brush(isDark ? "#FCA5A5" : "#A52632");
    }

    private static (string Symbol, SolidColorBrush Foreground, SolidColorBrush Background) GetIconVisuals(MessageBoxImage icon, bool isDark, bool isOriginal)
    {
        static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
        return icon switch
        {
            MessageBoxImage.Error => ("!", Brush(isDark ? "#FCA5A5" : "#DC2626"), Brush(isDark ? "#2D161B" : "#FEE2E2")),
            MessageBoxImage.Warning => ("⚠", Brush(isDark ? "#FCD34D" : "#D97706"), Brush(isDark ? "#2E2312" : "#FEF3C7")),
            MessageBoxImage.Question => ("؟", Brush(isOriginal ? (isDark ? "#60A5FA" : "#235DF6") : (isDark ? "#5EEAD4" : "#0F766E")), Brush(isOriginal ? (isDark ? "#172554" : "#DBEAFE") : (isDark ? "#112E30" : "#E0F2FE"))),
            _ => ("✓", Brush(isOriginal ? (isDark ? "#60A5FA" : "#235DF6") : (isDark ? "#5EEAD4" : "#0F766E")), Brush(isOriginal ? (isDark ? "#172554" : "#DBEAFE") : (isDark ? "#102E2E" : "#CCFBF1")))
        };
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
    public TextBox Input(string label, string value = "", bool numeric = false, string hint = "") => Field(label, new TextBox { Text = value, Tag = hint, Style = (Style)FindResource("WorkspaceInput"), FlowDirection = numeric ? FlowDirection.LeftToRight : FlowDirection.RightToLeft });
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
