using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WhySave.App.ViewModels;

public enum MainTab
{
    Search,
    Inbox,
    Library,
}

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private int _selectedTabIndex;

    public SearchViewModel Search { get; }
    public InboxViewModel Inbox { get; }
    public LibraryViewModel Library { get; }

    public MainViewModel(SearchViewModel search, InboxViewModel inbox, LibraryViewModel library)
    {
        Search = search;
        Inbox = inbox;
        Library = library;
    }

    public void SelectTab(MainTab tab)
    {
        SelectedTabIndex = (int)tab;
        if (tab == MainTab.Inbox)
            Inbox.Refresh();
        else if (tab == MainTab.Library)
            Library.Refresh();
    }

    [RelayCommand]
    private void RefreshCurrentTab()
    {
        switch ((MainTab)SelectedTabIndex)
        {
            case MainTab.Inbox:
                Inbox.Refresh();
                break;
            case MainTab.Library:
                Library.Refresh();
                break;
        }
    }
}
