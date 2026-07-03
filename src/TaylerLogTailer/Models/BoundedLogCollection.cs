using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace TaylerLogTailer.Models;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> of log rows that can drop its oldest
/// rows in a single batched notification.
///
/// The combined view keeps a bounded number of rows (see
/// <see cref="FolderConfig.MaxRows"/>). Trimming one row at a time through
/// <see cref="ObservableCollection{T}.RemoveAt"/> raises a collection-changed
/// event per removed row, and each event drives a layout pass in the bound
/// <c>DataGrid</c>. Once the view is at its cap and log lines arrive quickly,
/// that is thousands of events per poll, which saturates the UI thread and makes
/// the display appear to stop updating. <see cref="TrimHead"/> removes the whole
/// overflow with a single reset notification instead.
/// </summary>
public sealed class BoundedLogCollection : ObservableCollection<LogRow>
{
    /// <summary>
    /// Removes the oldest rows so that at most <paramref name="max"/> remain,
    /// raising a single reset notification rather than one event per row.
    /// Returns the number of rows removed.
    /// </summary>
    public int TrimHead(int max)
    {
        if (max < 0 || Count <= max)
        {
            return 0;
        }

        int remove = Count - max;
        CheckReentrancy();

        // Mutate the backing list directly so no per-row events are raised, then
        // announce the change once.
        if (Items is List<LogRow> list)
        {
            list.RemoveRange(0, remove);
        }
        else
        {
            for (int i = 0; i < remove; i++)
            {
                Items.RemoveAt(0);
            }
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

        return remove;
    }
}
