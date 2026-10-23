using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MyClinic;

public static class ScrollState
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(ScrollState), new PropertyMetadata(false, EnabledChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(State), typeof(ScrollState));
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    private static void EnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement element) return;
        if ((bool)e.NewValue) Ensure(element);
        else { (element.GetValue(StateProperty) as State)?.Dispose(); element.ClearValue(StateProperty); }
    }
    private static State Ensure(FrameworkElement element)
    {
        if (element.GetValue(StateProperty) is State state) return state;
        state = new State(element); element.SetValue(StateProperty, state); return state;
    }
    public static IDisposable Updating(FrameworkElement element, string key = "") => Ensure(element).Update(key);
    private static ScrollViewer? Find(DependencyObject parent)
    {
        if (parent is ScrollViewer viewer) return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var result = Find(VisualTreeHelper.GetChild(parent, i));
            if (result != null) return result;
        }
        return null;
    }
    private sealed class State : IDisposable
    {
        private readonly FrameworkElement _element;
        private readonly Dictionary<string, double> _offsets = new();
        private string _key = "";
        private int _version;
        private bool _updating;
        private bool _restoring;
        public State(FrameworkElement element)
        {
            _element = element;
            element.Loaded += Loaded; element.Unloaded += Unloaded;
            element.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Scrolled), true);
        }
        private void Capture()
        {
            if (Find(_element) is ScrollViewer scroll)
                _offsets[_key] = scroll.VerticalOffset;
        }
        private void Scrolled(object sender, ScrollChangedEventArgs e)
        {
            if (!_updating && !_restoring && e.OriginalSource is ScrollViewer viewer && ReferenceEquals(viewer, Find(_element)))
                _offsets[_key] = viewer.VerticalOffset;
        }
        public IDisposable Update(string key)
        {
            Capture(); _key = key; _updating = true;
            int version = ++_version;
            return new UpdateScope(() => { _updating = false; Restore(version); });
        }
        private void Loaded(object sender, RoutedEventArgs e) => Restore(++_version);
        private void Unloaded(object sender, RoutedEventArgs e) { if (!_updating) Capture(); ++_version; }
        private void Restore(int version)
        {
            _restoring = true;
            _element.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (version == _version && _element.IsLoaded && Find(_element) is ScrollViewer viewer)
                    viewer.ScrollToVerticalOffset(_offsets.GetValueOrDefault(_key));
                if (version == _version) _restoring = false;
            }));
        }
        public void Dispose()
        {
            ++_version; _element.Loaded -= Loaded; _element.Unloaded -= Unloaded;
            _element.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Scrolled));
        }
    }
    private sealed class UpdateScope(Action complete) : IDisposable { public void Dispose() => complete(); }
}
