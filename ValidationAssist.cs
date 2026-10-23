using System.Globalization;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MyClinic;

public static class ValidationAssist
{
    public static readonly DependencyProperty ErrorProperty = DependencyProperty.RegisterAttached(
        "Error", typeof(string), typeof(ValidationAssist), new PropertyMetadata("", ErrorChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(ErrorState), typeof(ValidationAssist));
    public static string GetError(DependencyObject element) => (string)element.GetValue(ErrorProperty);
    public static void SetError(DependencyObject element, string value) => element.SetValue(ErrorProperty, value);
    public static bool Check(FrameworkElement input, bool valid, string message)
    {
        SetError(input, valid ? "" : message);
        return valid;
    }
    public static void Show(FrameworkElement input, string message)
    {
        SetError(input, message);
        FocusFirst(input);
    }
    public static void FocusFirst(params FrameworkElement[] inputs)
    {
        var first = inputs.FirstOrDefault(input => !string.IsNullOrEmpty(GetError(input)));
        if (first == null) return;
        for (DependencyObject? parent = first.Parent; parent != null;
             parent = parent is FrameworkElement element ? element.Parent ?? VisualTreeHelper.GetParent(element) : null)
            if (parent is Expander expander) expander.IsExpanded = true;
        first.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            first.BringIntoView();
            first.Focus();
            Keyboard.Focus(first);
        }));
    }
    private static void ErrorChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement input) return;
        (input.GetValue(StateProperty) as ErrorState)?.Dispose();
        input.ClearValue(StateProperty);
        if (!string.IsNullOrEmpty((string)e.NewValue))
            input.SetValue(StateProperty, new ErrorState(input, (string)e.NewValue));
    }
    private sealed class ErrorState : IDisposable
    {
        private readonly FrameworkElement _input;
        private readonly Thickness _margin;
        private readonly string _message;
        private readonly string _helpText;
        private ErrorAdorner? _adorner;
        private AdornerLayer? _layer;
        public ErrorState(FrameworkElement input, string message)
        {
            _input = input; _message = message; _margin = input.Margin;
            _helpText = AutomationProperties.GetHelpText(input);
            AutomationProperties.SetHelpText(input, message);
            input.Loaded += Loaded; input.Unloaded += Unloaded;
            input.IsVisibleChanged += VisibleChanged;
            input.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(Edited), true);
            input.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(Selected), true);
            if (input is DatePicker picker) picker.SelectedDateChanged += DateChanged;
            if (input.IsLoaded) Attach();
        }
        private void Loaded(object sender, RoutedEventArgs e) => Attach();
        private void Attach()
        {
            _layer = AdornerLayer.GetAdornerLayer(_input);
            if (_layer == null || _adorner != null) return;
            _adorner = new ErrorAdorner(_input, _message);
            _layer.Add(_adorner);
            double width = Math.Max(100, _input.ActualWidth);
            double extra = Math.Max(24, _adorner.MessageHeight(width) + 6);
            _input.SetCurrentValue(FrameworkElement.MarginProperty, new Thickness(_margin.Left, _margin.Top, _margin.Right, _margin.Bottom + extra));
        }
        private void Edited(object sender, TextChangedEventArgs e) => SetError(_input, "");
        private void Selected(object sender, SelectionChangedEventArgs e) => SetError(_input, "");
        private void DateChanged(object? sender, SelectionChangedEventArgs e) => SetError(_input, "");
        private void Unloaded(object sender, RoutedEventArgs e) => Detach();
        private void VisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue) Attach(); else Detach();
        }
        private void Detach()
        {
            if (_adorner != null) _layer?.Remove(_adorner);
            _adorner = null; _layer = null;
            _input.SetCurrentValue(FrameworkElement.MarginProperty, _margin);
        }
        public void Dispose()
        {
            Detach();
            AutomationProperties.SetHelpText(_input, _helpText);
            _input.Loaded -= Loaded; _input.Unloaded -= Unloaded;
            _input.IsVisibleChanged -= VisibleChanged;
            _input.RemoveHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(Edited));
            _input.RemoveHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(Selected));
            if (_input is DatePicker picker) picker.SelectedDateChanged -= DateChanged;
        }
    }
    private sealed class ErrorAdorner : Adorner
    {
        private readonly string _message;
        public ErrorAdorner(UIElement input, string message) : base(input)
        {
            _message = message; IsHitTestVisible = false;
        }
        private FormattedText Text(double width) => new(_message, CultureInfo.CurrentUICulture,
            FlowDirection.RightToLeft, new Typeface("Segoe UI"), 12, Brushes.IndianRed, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = Math.Max(100, width), TextAlignment = TextAlignment.Right };
        public double MessageHeight(double width) => Text(width).Height;
        protected override void OnRender(DrawingContext context)
        {
            Size size = AdornedElement.RenderSize;
            context.DrawRoundedRectangle(null, new Pen(Brushes.IndianRed, 1.5), new Rect(0, 0, size.Width, size.Height), 6, 6);
            context.DrawText(Text(size.Width), new Point(0, size.Height + 4));
        }
    }
}
