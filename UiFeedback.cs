using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace MyClinic;

public static class UiFeedback
{
    private static readonly DependencyProperty HostProperty = DependencyProperty.RegisterAttached("Host", typeof(ToastHost), typeof(UiFeedback));

    public static void Notify(DependencyObject source, string message)
    {
        Window? window = source as Window ?? Window.GetWindow(source) ?? Application.Current?.MainWindow;
        window = window?.Owner ?? window;
        if (window?.Content is not Panel root) return;
        if (window.GetValue(HostProperty) is not ToastHost host)
        {
            host = new ToastHost(window, root);
            window.SetValue(HostProperty, host);
        }
        host.Show(message);
    }

    private sealed class ToastHost
    {
        private readonly StackPanel _panel = new() { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(24), MaxWidth = 440, FlowDirection = FlowDirection.RightToLeft };
        private readonly Dictionary<Border, DispatcherTimer> _timers = new();
        public ToastHost(Window window, Panel root)
        {
            Panel.SetZIndex(_panel, 10000);
            Grid.SetRowSpan(_panel, int.MaxValue);
            Grid.SetColumnSpan(_panel, int.MaxValue);
            root.Children.Add(_panel);
            window.Closed += (_, _) =>
            {
                foreach (var timer in _timers.Values) timer.Stop();
                _timers.Clear();
                _panel.Children.Clear();
            };
        }
        public void Show(string message)
        {
            while (_panel.Children.Count >= 3) Remove((Border)_panel.Children[0]);
            var text = new TextBlock { Text = "✓  " + message, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontSize = 14, Margin = new Thickness(0, 0, 12, 0) };
            text.SetResourceReference(TextBlock.ForegroundProperty, "AppTextPrimary");
            AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
            text.Loaded += (_, _) => UIElementAutomationPeer.CreatePeerForElement(text)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            var close = new Button { Content = "×", Width = 28, Height = 28, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
            close.SetResourceReference(Control.ForegroundProperty, "AppTextSecondary");
            AutomationProperties.SetName(close, "إغلاق الإشعار");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(close, 1);
            grid.Children.Add(text); grid.Children.Add(close);
            var card = new Border { Child = grid, Padding = new Thickness(16, 12, 12, 12), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 6, 0, 0), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "AppCardBg");
            card.SetResourceReference(Border.BorderBrushProperty, "DashCalendarTodayNumFg");
            AutomationProperties.SetLiveSetting(card, AutomationLiveSetting.Polite);
            AutomationProperties.SetName(card, message);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) => Remove(card);
            card.MouseEnter += (_, _) => timer.Stop();
            card.MouseLeave += (_, _) => timer.Start();
            card.GotKeyboardFocus += (_, _) => timer.Stop();
            card.LostKeyboardFocus += (_, _) => timer.Start();
            close.Click += (_, _) => Remove(card);
            _timers.Add(card, timer); _panel.Children.Add(card); timer.Start();
            ChartMotion.FadeIn(card);
        }
        private void Remove(Border card)
        {
            if (_timers.Remove(card, out var timer)) timer.Stop();
            _panel.Children.Remove(card);
        }
    }
}

// Presentation state only: existing save handlers retain their validation and persistence rules.
public sealed class SaveFeedback : IDisposable
{
    private static readonly DependencyProperty OperationProperty = DependencyProperty.RegisterAttached("Operation", typeof(SaveFeedback), typeof(SaveFeedback));
    private static readonly DependencyProperty LabelProperty = DependencyProperty.RegisterAttached("Label", typeof(LabelState), typeof(SaveFeedback));
    private readonly FrameworkElement _owner;
    private readonly List<(Button Button, object? Content, bool Enabled)> _buttons = new();
    private readonly bool _ownerEnabled;
    private bool _succeeded;
    public bool CanStart { get; }
    private SaveFeedback(FrameworkElement owner, object? sender, Button[] peers)
    {
        _owner = owner;
        if (owner.GetValue(OperationProperty) != null) return;
        CanStart = true;
        _ownerEnabled = owner.IsEnabled;
        owner.SetValue(OperationProperty, this);
        foreach (var button in peers.Concat(new[] { sender as Button }).OfType<Button>().Distinct())
        {
            (button.GetValue(LabelProperty) as LabelState)?.Restore();
            _buttons.Add((button, button.Content, button.IsEnabled));
            button.SetCurrentValue(ContentControl.ContentProperty, button.ActualWidth is > 0 and < 100 ? "…" : "جارٍ الحفظ...");
            AutomationProperties.SetItemStatus(button, "جارٍ الحفظ...");
            button.SetCurrentValue(UIElement.IsEnabledProperty, false);
        }
        // Keep the submitted fields stable while the handler yields to paint its busy state.
        owner.SetCurrentValue(UIElement.IsEnabledProperty, false);
    }
    public static SaveFeedback Begin(FrameworkElement owner, object? sender, params Button[] peers) => new(owner, sender, peers);
    public async Task ShowBusyAsync() => await Dispatcher.Yield(DispatcherPriority.Background);
    public void Success(string message)
    {
        _succeeded = true;
        UiFeedback.Notify(_owner, message);
    }
    public void Dispose()
    {
        if (!CanStart) return;
        _owner.ClearValue(OperationProperty);
        foreach (var (button, content, enabled) in _buttons)
        {
            button.SetCurrentValue(UIElement.IsEnabledProperty, enabled);
            AutomationProperties.SetItemStatus(button, "");
            if (_succeeded && button.IsLoaded)
            {
                var state = new LabelState(button, content);
                button.SetValue(LabelProperty, state);
                state.Show();
            }
            else button.SetCurrentValue(ContentControl.ContentProperty, content);
        }
        _owner.SetCurrentValue(UIElement.IsEnabledProperty, _ownerEnabled);
    }
    private sealed class LabelState
    {
        private readonly Button _button;
        private readonly object? _content;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1400) };
        public LabelState(Button button, object? content)
        {
            _button = button; _content = content;
            _timer.Tick += (_, _) => Restore();
        }
        public void Show()
        {
            _button.SetCurrentValue(ContentControl.ContentProperty, _button.ActualWidth is > 0 and < 100 ? "✓" : "✓ تم الحفظ");
            AutomationProperties.SetItemStatus(_button, "تم الحفظ");
            _button.Unloaded += Unloaded;
            _timer.Start();
        }
        private void Unloaded(object sender, RoutedEventArgs e) => Restore();
        public void Restore()
        {
            _timer.Stop(); _button.Unloaded -= Unloaded;
            _button.SetCurrentValue(ContentControl.ContentProperty, _content);
            AutomationProperties.SetItemStatus(_button, "");
            _button.ClearValue(LabelProperty);
        }
    }
}
