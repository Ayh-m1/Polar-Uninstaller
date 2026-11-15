using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace PolarUninstallerWpf;

/// <summary>One reset per scan batch; never mutate a live collection inside DeferRefresh.</summary>
internal sealed class DuplicateCardCollection:ObservableCollection<DuplicateGroupCard>
{
    public void ReplaceWith(IEnumerable<DuplicateGroupCard> replacement)
    {Items.Clear();foreach(var item in replacement)Items.Add(item);Reset();}
    public void AddRange(IEnumerable<DuplicateGroupCard> added)
    {foreach(var item in added)Items.Add(item);Reset();}
    private void Reset(){OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));}
}
