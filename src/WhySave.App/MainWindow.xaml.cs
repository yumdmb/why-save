using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (e.Source is not TabControl)
            return;

        switch ((MainTab)vm.SelectedTabIndex)
        {
            case MainTab.Find:
                _ = vm.Find.RefreshAsync();
                break;
            case MainTab.Inbox:
                vm.Inbox.Refresh();
                break;
        }
    }

    private void InboxListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        foreach (var removed in e.RemovedItems.OfType<FileRowViewModel>())
            vm.Inbox.SelectedItems.Remove(removed);

        foreach (var added in e.AddedItems.OfType<FileRowViewModel>())
            vm.Inbox.SelectedItems.Add(added);
    }
}
