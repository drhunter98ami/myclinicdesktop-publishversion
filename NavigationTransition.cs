using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MyClinic;

public enum NavigationMotion { None, Header, Cards, Card, Toolbar, Content, Items }

// Explicit page choreography keeps motion out of popups, editors and control templates.
public static class NavigationTransition
{
    public static readonly DependencyProperty MotionProperty = DependencyProperty.RegisterAttached(
        "Motion", typeof(NavigationMotion), typeof(NavigationTransition), new PropertyMetadata(NavigationMotion.None));
    public static readonly DependencyProperty OrderProperty = DependencyProperty.RegisterAttached(
        "Order", typeof(int), typeof(NavigationTransition), new PropertyMetadata(0));
    private static readonly DependencyProperty SessionProperty = DependencyProperty.RegisterAttached(
        "Session", typeof(EntranceSession), typeof(NavigationTransition));

    public static NavigationMotion GetMotion(DependencyObject element) => (NavigationMotion)element.GetValue(MotionProperty);
    public static void SetMotion(DependencyObject element, NavigationMotion value) => element.SetValue(MotionProperty, value);
    public static int GetOrder(DependencyObject element) => (int)element.GetValue(OrderProperty);
    public static void SetOrder(DependencyObject element, int value) => element.SetValue(OrderProperty, value);

    public static void Show(ContentControl host, object content)
    {
        if (ReferenceEquals(host.Content, content))
            return;

        (host.GetValue(SessionProperty) as EntranceSession)?.Stop();
        bool animate = host.IsLoaded && host.Content != null && MotionPreferences.Enabled;
        host.Content = content;
        if (!animate || content is not FrameworkElement page)
            return;

        var session = new EntranceSession(host, page);
        host.SetValue(SessionProperty, session);
        session.Queue();
    }

    private sealed class EntranceSession
    {
        private readonly ContentControl _host;
        private readonly FrameworkElement _page;
        private readonly List<Action> _restore = new();
        private readonly HashSet<FrameworkElement> _animated = new();
        private readonly List<(ItemsControl Items, EventHandler Handler)> _itemSubscriptions = new();
        private readonly Stopwatch _elapsed = new();
        private DispatcherTimer? _finishTimer;
        private bool _stopped;

        public EntranceSession(ContentControl host, FrameworkElement page) => (_host, _page) = (host, page);

        public void Queue()
        {
            _host.Unloaded += OnUnloaded;
            // Cover the layout pass, then reveal the independently animated page sections.
            _page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.Zero));
            _host.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Start));
        }

        private void Start()
        {
            if (_stopped)
                return;

            _page.BeginAnimation(UIElement.OpacityProperty, null);
            if (!MotionPreferences.Enabled)
            {
                Stop();
                return;
            }

            _elapsed.Start();
            Visit(_page);
            _finishTimer = new DispatcherTimer(DispatcherPriority.Background, _host.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(1400)
            };
            _finishTimer.Tick += OnFinished;
            _finishTimer.Start();
        }

        private void Visit(FrameworkElement element)
        {
            var motion = GetMotion(element);
            int delay = Math.Clamp(GetOrder(element), 0, 6) * 45;
            if (motion == NavigationMotion.Items && element is ItemsControl items)
                WatchItems(items, delay);
            else if (motion == NavigationMotion.Cards && element is Panel panel)
            {
                int index = 0;
                foreach (UIElement child in panel.Children)
                    if (child is FrameworkElement card && card.IsVisible)
                        Animate(card, NavigationMotion.Card, Math.Min(300, delay + index++ * 38));
            }
            else if (motion != NavigationMotion.None)
                Animate(element, motion, delay);

            // Items are handled through their generator; never enumerate an entire data source.
            if (element is ItemsControl)
                return;
            foreach (object child in LogicalTreeHelper.GetChildren(element))
                if (child is FrameworkElement frameworkChild)
                    Visit(frameworkChild);
        }

        private void WatchItems(ItemsControl items, int delay)
        {
            EventHandler handler = (_, _) =>
            {
                if (!_stopped && _elapsed.ElapsedMilliseconds < 700 &&
                    items.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
                    _host.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                        new Action(() => AnimateRows(items, 0)));
            };
            items.ItemContainerGenerator.StatusChanged += handler;
            _itemSubscriptions.Add((items, handler));
            AnimateRows(items, delay);
        }

        private void AnimateRows(ItemsControl items, int delay)
        {
            if (_stopped || _elapsed.ElapsedMilliseconds >= 700)
                return;
            Panel? panel = FindItemsPanel(items, items);
            if (panel == null)
                return;
            int index = 0;
            foreach (UIElement child in panel.Children)
            {
                if (child is FrameworkElement row && IsInViewport(row))
                {
                    Animate(row, NavigationMotion.Content, Math.Min(330, delay + index++ * 28), 8);
                    if (index == 8)
                        break;
                }
            }
        }

        private static Panel? FindItemsPanel(DependencyObject node, ItemsControl owner)
        {
            if (node is Panel panel && panel.IsItemsHost && ItemsControl.GetItemsOwner(panel) == owner)
                return panel;
            // Do not descend into generated rows or nested collections.
            if (node != owner && node is ItemsControl)
                return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var found = FindItemsPanel(VisualTreeHelper.GetChild(node, i), owner);
                if (found != null)
                    return found;
            }
            return null;
        }

        private bool IsInViewport(FrameworkElement element)
        {
            if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
                return false;
            Rect bounds = element.TransformToAncestor(_host).TransformBounds(new Rect(element.RenderSize));
            if (!bounds.IntersectsWith(new Rect(_host.RenderSize)))
                return false;
            for (DependencyObject? parent = VisualTreeHelper.GetParent(element);
                 parent != null && parent != _host; parent = VisualTreeHelper.GetParent(parent))
                if (parent is ScrollContentPresenter viewport &&
                    !bounds.IntersectsWith(viewport.TransformToAncestor(_host).TransformBounds(new Rect(viewport.RenderSize))))
                    return false;
            return true;
        }

        private void Animate(FrameworkElement element, NavigationMotion motion, int delay, double? distance = null)
        {
            if (!IsInViewport(element) || !_animated.Add(element))
                return;

            // Keep any existing transform/binding, and restore it after completion or interruption.
            Transform original = element.RenderTransform;
            var translation = new TranslateTransform();
            var scale = new ScaleTransform(1, 1, element.ActualWidth / 2, element.ActualHeight / 2);
            var group = new TransformGroup();
            group.Children.Add(original);
            group.Children.Add(scale);
            group.Children.Add(translation);
            element.SetCurrentValue(UIElement.RenderTransformProperty, group);
            _restore.Add(() =>
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                translation.BeginAnimation(TranslateTransform.XProperty, null);
                translation.BeginAnimation(TranslateTransform.YProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                group.Children.Remove(scale);
                group.Children.Remove(translation);
                if (ReferenceEquals(element.RenderTransform, group))
                    element.SetCurrentValue(UIElement.RenderTransformProperty, original);
                else if (element.RenderTransform is TransformGroup outer && outer.Children.Contains(group))
                    outer.Children[outer.Children.IndexOf(group)] = original;
            });

            int duration = motion == NavigationMotion.Header ? 380 : 440;
            element.BeginAnimation(UIElement.OpacityProperty, Track(0, element.Opacity, delay, 300));
            if (motion == NavigationMotion.Toolbar)
                translation.BeginAnimation(TranslateTransform.XProperty,
                    Track(MotionPreferences.Distance(element.FlowDirection == FlowDirection.RightToLeft ? -14 : 14), 0, delay, duration));
            else
                translation.BeginAnimation(TranslateTransform.YProperty,
                    Track(MotionPreferences.Distance(distance ?? (motion == NavigationMotion.Header ? 14 : 24)), 0, delay, duration));
            if (motion == NavigationMotion.Card)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, Track(0.985, 1, delay, duration));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, Track(0.985, 1, delay, duration));
            }
        }

        private static DoubleAnimationUsingKeyFrames Track(double from, double to, int delay, int duration)
        {
            var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if (delay > 0)
                animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(MotionPreferences.Duration(delay))));
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(to,
                KeyTime.FromTimeSpan(MotionPreferences.Duration(delay + duration)), new KeySpline(0.16, 1, 0.3, 1)));
            return animation;
        }

        private void OnFinished(object? sender, EventArgs e) => Stop();
        private void OnUnloaded(object sender, RoutedEventArgs e) => Stop();

        public void Stop()
        {
            if (_stopped)
                return;
            _stopped = true;
            _host.Unloaded -= OnUnloaded;
            if (_finishTimer != null)
            {
                _finishTimer.Stop();
                _finishTimer.Tick -= OnFinished;
            }
            foreach (var (items, handler) in _itemSubscriptions)
                items.ItemContainerGenerator.StatusChanged -= handler;
            _page.BeginAnimation(UIElement.OpacityProperty, null);
            foreach (Action restore in _restore)
                restore();
            _restore.Clear();
            _animated.Clear();
            if (ReferenceEquals(_host.GetValue(SessionProperty), this))
                _host.ClearValue(SessionProperty);
        }
    }
}
