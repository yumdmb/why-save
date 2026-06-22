using System.Windows;
using WhySave.App.ViewModels;

namespace WhySave.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public void SelectTab(MainTab tab)
    {
        if (DataContext is MainViewModel vm)
            vm.SelectTab(tab);
    }

    private void InboxListView_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        foreach (var removed in e.RemovedItems.OfType<FileRowViewModel>())
            vm.Inbox.SelectedItems.Remove(removed);

        foreach (var added in e.AddedItems.OfType<FileRowViewModel>())
            vm.Inbox.SelectedItems.Add(added);
    }
}
