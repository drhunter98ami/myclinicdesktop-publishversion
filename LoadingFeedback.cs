using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MyClinic;

public static class LoadingFeedback
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(LoadingState), typeof(LoadingFeedback));
    public static void Set(FrameworkElement owner, Border overlay, bool loading, bool hasContent)
    {
        if (overlay.GetValue(StateProperty) is not LoadingState state)
        {
            state = new LoadingState(owner, overlay);
            overlay.SetValue(StateProperty, state);
        }
        state.Set(loading, hasContent);
    }
    private sealed class LoadingState
    {
        private readonly Border _overlay;
        private readonly Brush _background;
        private readonly DispatcherTimer _delay = new() { Interval = TimeSpan.FromMilliseconds(150) };
        private bool _pending;
        public LoadingState(FrameworkElement owner, Border overlay)
        {
            _overlay = overlay; _background = overlay.Background;
            _delay.Tick += (_, _) => { _delay.Stop(); if (_pending) _overlay.Visibility = Visibility.Visible; };
            owner.Unloaded += (_, _) => { _delay.Stop(); _overlay.Visibility = Visibility.Collapsed; };
        }
        public void Set(bool loading, bool hasContent)
        {
            _pending = loading; _delay.Stop();
            if (!loading)
            {
                _overlay.Visibility = Visibility.Collapsed;
                _overlay.Child = null;
                return;
            }
            _overlay.Visibility = Visibility.Collapsed;
            _overlay.Background = hasContent ? Brushes.Transparent : _background;
            _overlay.IsHitTestVisible = !hasContent;
            var body = new StackPanel { Margin = new Thickness(20), MaxWidth = hasContent ? 260 : 420, HorizontalAlignment = hasContent ? HorizontalAlignment.Right : HorizontalAlignment.Center, VerticalAlignment = hasContent ? VerticalAlignment.Top : VerticalAlignment.Center };
            var title = new TextBlock { Text = hasContent ? "جارٍ تحديث البيانات..." : "جارٍ تحميل السجلات...", FontSize = 14, Margin = new Thickness(0, 0, 0, 10), TextAlignment = TextAlignment.Center };
            title.SetResourceReference(TextBlock.ForegroundProperty, "AppTextPrimary");
            body.Children.Add(title);
            if (!hasContent)
                foreach (int width in new[] { 330, 280, 310 })
                {
                    var line = new Rectangle { Width = width, Height = 18, RadiusX = 6, RadiusY = 6, Opacity = 0.18, Margin = new Thickness(0, 7, 0, 7) };
                    line.SetResourceReference(Shape.FillProperty, "AppTextSecondary"); body.Children.Add(line);
                }
            var progress = new ProgressBar { Height = 3, IsIndeterminate = MotionPreferences.Enabled, Value = MotionPreferences.Enabled ? 0 : 60, Margin = new Thickness(0, 8, 0, 0) };
            body.Children.Add(progress);
            var card = new Border { Child = body, CornerRadius = new CornerRadius(12), HorizontalAlignment = hasContent ? HorizontalAlignment.Right : HorizontalAlignment.Center, VerticalAlignment = hasContent ? VerticalAlignment.Top : VerticalAlignment.Center };
            card.SetResourceReference(Border.BackgroundProperty, "AppCardBg");
            _overlay.Child = card;
            _delay.Start();
        }
    }
}
