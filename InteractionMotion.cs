using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MyClinic;

// Inherited by page controls, including generated rows and controls in templates.
public static class InteractionMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(InteractionMotion),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnEnabledChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(Feedback), typeof(InteractionMotion));

    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement element)
            return;
        if (!(bool)e.NewValue)
        {
            (element.GetValue(StateProperty) as Feedback)?.Dispose();
            element.ClearValue(StateProperty);
            return;
        }

        // The owning input already supplies feedback for its template parts.
        if (element.TemplatedParent is TextBoxBase or PasswordBox or ComboBox or DatePicker or ScrollBar)
            return;

        bool card = NavigationTransition.GetMotion(element) == NavigationMotion.Card ||
                    element.Parent is Panel parent && NavigationTransition.GetMotion(parent) == NavigationMotion.Cards;
        if (element is ButtonBase or TextBoxBase or PasswordBox or ComboBox or DatePicker or DataGridRow or ListBoxItem || card)
            element.SetValue(StateProperty, new Feedback(element, card));
    }

    private sealed class Feedback
    {
        private readonly FrameworkElement _element;
        private readonly bool _moves;
        private readonly bool _row;
        private Transform? _original;
        private TransformGroup? _group;
        private ScaleTransform? _scale;
        private TranslateTransform? _offset;
        private Highlight? _highlight;
        private AdornerLayer? _layer;

        public Feedback(FrameworkElement element, bool card)
        {
            _element = element;
            // Template buttons inside inputs keep the input's geometry steady.
            _moves = card || element is ButtonBase && element.TemplatedParent is not ComboBox and not DatePicker and not ScrollBar;
            _row = element is DataGridRow or ListBoxItem;
            element.MouseEnter += Update;
            element.MouseLeave += Update;
            element.GotKeyboardFocus += FocusChanged;
            element.LostKeyboardFocus += FocusChanged;
            element.IsEnabledChanged += EnabledChanged;
            element.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(Clicked), true);
            element.IsVisibleChanged += VisibleChanged;
            element.PreviewMouseDown += PointerChanged;
            element.PreviewMouseUp += PointerChanged;
            element.LostMouseCapture += Update;
            element.PreviewKeyDown += KeyChanged;
            element.PreviewKeyUp += KeyChanged;
            element.Unloaded += Unloaded;
        }

        private void Update(object sender, MouseEventArgs e) => Refresh();
        private void FocusChanged(object sender, KeyboardFocusChangedEventArgs e) => Refresh();
        private void EnabledChanged(object sender, DependencyPropertyChangedEventArgs e) => Refresh();
        private void VisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!_element.IsVisible) Reset();
        }
        private void PointerChanged(object sender, MouseButtonEventArgs e) => Refresh(e.ButtonState == MouseButtonState.Pressed);
        private void KeyChanged(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && _element is ButtonBase)
                Refresh(e.IsDown);
        }
        private void Clicked(object sender, RoutedEventArgs e) => Refresh(false);

        private void Refresh(bool pressed = false)
        {
            bool hover = _element.IsMouseOver && _element.IsEnabled;
            bool focus = _element.IsKeyboardFocusWithin && _element.IsEnabled;
            if (_moves && _element.IsLoaded && (MotionPreferences.Enabled || _group != null))
            {
                EnsureTransform();
                bool press = MotionPreferences.Enabled && _element is ButtonBase && pressed && (hover || focus);
                double size = press ? (MotionPreferences.Current == MotionLevel.Subtle ? 0.99 : 0.98) : 1;
                _scale!.CenterX = _element.ActualWidth / 2;
                _scale.CenterY = _element.ActualHeight / 2;
                Animate(_scale, ScaleTransform.ScaleXProperty, size);
                Animate(_scale, ScaleTransform.ScaleYProperty, size);
                Animate(_offset!, TranslateTransform.YProperty, MotionPreferences.Enabled && hover && !press ? -MotionPreferences.Distance(2) : 0);
            }
            if ((hover || focus) && _highlight == null && _element.IsLoaded)
            {
                _layer = AdornerLayer.GetAdornerLayer(_element);
                if (_layer != null)
                {
                    _highlight = new Highlight(_element, _row);
                    _layer.Add(_highlight);
                }
            }
            if (_highlight != null)
            {
                _highlight.Focused = focus;
                _highlight.InvalidateVisual();
                Animate(_highlight, UIElement.OpacityProperty, focus ? 1 : hover ? 0.55 : 0);
            }
        }

        private void EnsureTransform()
        {
            if (_group != null) return;
            _original = _element.RenderTransform;
            _scale = new ScaleTransform();
            _offset = new TranslateTransform();
            _group = new TransformGroup();
            _group.Children.Add(_original);
            _group.Children.Add(_scale);
            _group.Children.Add(_offset);
            _element.SetCurrentValue(UIElement.RenderTransformProperty, _group);
        }

        private void Unloaded(object sender, RoutedEventArgs e) => Reset();
        private void Reset()
        {
            if (_highlight != null) _layer?.Remove(_highlight);
            _highlight = null;
            _layer = null;
            _scale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _scale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _offset?.BeginAnimation(TranslateTransform.YProperty, null);
            if (_group != null)
            {
                _group.Children.Remove(_scale!);
                _group.Children.Remove(_offset!);
                if (ReferenceEquals(_element.RenderTransform, _group))
                    _element.SetCurrentValue(UIElement.RenderTransformProperty, _original);
                else if (_element.RenderTransform is TransformGroup outer && outer.Children.Contains(_group))
                    outer.Children[outer.Children.IndexOf(_group)] = _original!;
            }
            _group = null;
        }

        public void Dispose()
        {
            Reset();
            _element.MouseEnter -= Update;
            _element.MouseLeave -= Update;
            _element.GotKeyboardFocus -= FocusChanged;
            _element.LostKeyboardFocus -= FocusChanged;
            _element.IsEnabledChanged -= EnabledChanged;
            _element.RemoveHandler(ButtonBase.ClickEvent, new RoutedEventHandler(Clicked));
            _element.IsVisibleChanged -= VisibleChanged;
            _element.PreviewMouseDown -= PointerChanged;
            _element.PreviewMouseUp -= PointerChanged;
            _element.LostMouseCapture -= Update;
            _element.PreviewKeyDown -= KeyChanged;
            _element.PreviewKeyUp -= KeyChanged;
            _element.Unloaded -= Unloaded;
        }
    }

    private sealed class Highlight : Adorner
    {
        private readonly bool _row;
        public bool Focused { get; set; }
        public Highlight(UIElement element, bool row) : base(element)
        {
            _row = row;
            IsHitTestVisible = false;
            Opacity = 0;
        }
        protected override void OnRender(DrawingContext drawingContext)
        {
            var accent = (AdornedElement as FrameworkElement)?.TryFindResource("DashCalendarTodayNumFg") as SolidColorBrush;
            Brush brush = accent ?? Brushes.MediumTurquoise;
            Rect bounds = new(1, 1, Math.Max(0, AdornedElement.RenderSize.Width - 2), Math.Max(0, AdornedElement.RenderSize.Height - 2));
            if (_row)
            {
                drawingContext.PushOpacity(0.10);
                drawingContext.DrawRoundedRectangle(brush, null, bounds, 5, 5);
                drawingContext.Pop();
            }
            drawingContext.DrawRoundedRectangle(null, new Pen(brush, Focused ? 2 : 1), bounds, _row ? 5 : 8, _row ? 5 : 8);
        }
    }

    private static void Animate(DependencyObject target, DependencyProperty property, double value)
    {
        ChartMotion.To(target, property, value);
    }
}
