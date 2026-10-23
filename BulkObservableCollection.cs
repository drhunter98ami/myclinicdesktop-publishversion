using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace MyClinic;

// Refresh a bound list with one notification instead of rebuilding it once per row.
internal sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        var snapshot = items.ToList();
        if (Count == snapshot.Count && this.SequenceEqual(snapshot))
            return;

        CheckReentrancy();
        Items.Clear();
        foreach (T item in snapshot)
            Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
