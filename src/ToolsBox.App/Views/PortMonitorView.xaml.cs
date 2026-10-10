using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ToolsBox.App.Views;

public partial class PortMonitorView : UserControl
{
    public PortMonitorView() => InitializeComponent();

    private void OnSortByField(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string field } || PortGrid.ItemsSource is null) return;
        var view = CollectionViewSource.GetDefaultView(PortGrid.ItemsSource);
        if (!view.CanSort) return;
        var direction = view.SortDescriptions.Count == 1 &&
            view.SortDescriptions[0].PropertyName == field &&
            view.SortDescriptions[0].Direction == ListSortDirection.Ascending
            ? ListSortDirection.Descending : ListSortDirection.Ascending;
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(field, direction));
        }
        foreach (var column in PortGrid.Columns)
            column.SortDirection = column.SortMemberPath == field ? direction : null;
        e.Handled = true;
    }
}
