using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using WhySave.Storage.Repositories;

namespace WhySave.App.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly FilesRepository _filesRepository;
    private readonly ILogger _logger;

    [ObservableProperty]
    private ObservableCollection<FileRowViewModel> _items = new();

    [ObservableProperty]
    private string _browseFilter = "";

    [ObservableProperty]
    private bool _isEmpty = true;

    public LibraryViewModel(FilesRepository filesRepository, ILogger logger)
    {
        _filesRepository = filesRepository;
        _logger = logger;
    }

    public void Refresh()
    {
        Items.Clear();
        var legacy = _filesRepository.ListByStatus("legacy");
        var contexted = _filesRepository.ListByStatus("contexted");
        var all = legacy.Concat(contexted)
            .OrderByDescending(r => r.SavedAt)
            .ThenBy(r => r.Filename);

        foreach (var record in all)
        {
            Items.Add(new FileRowViewModel(record));
        }

        IsEmpty = Items.Count == 0;
        _logger.Information("Library refreshed; {Count} items", Items.Count);
    }

    [RelayCommand]
    private void ApplyFilter()
    {
        _logger.Information("Library filter applied: {Filter}", BrowseFilter);
        // Filtering by filename is applied in the view via CollectionView in a later polish pass.
    }
}
