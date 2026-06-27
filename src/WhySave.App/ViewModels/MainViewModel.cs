using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WhySave.App.ViewModels;

public enum MainTab
{
    Find,
    Inbox,
}

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private int _selectedTabIndex;

    public SearchViewModel Find { get; }
    public InboxViewModel Inbox { get; }

    public MainViewModel(SearchViewModel find, InboxViewModel inbox)
    {
        Find = find;
        Inbox = inbox;
    }

    public void SelectTab(MainTab tab)
    {
        SelectedTabIndex = (int)tab;
        if (tab == MainTab.Find)
            _ = Find.RefreshAsync();
        else if (tab == MainTab.Inbox)
            Inbox.Refresh();
    }

    [RelayCommand]
    private void RefreshCurrentTab()
    {
        switch ((MainTab)SelectedTabIndex)
        {
            case MainTab.Find:
                _ = Find.RefreshAsync();
                break;
            case MainTab.Inbox:
                Inbox.Refresh();
                break;
        }
    }
}
