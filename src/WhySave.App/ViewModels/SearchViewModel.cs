using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using WhySave.Storage.Repositories;

namespace WhySave.App.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly FilesRepository _filesRepository;
    private readonly ILogger _logger;

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private IReadOnlyList<FileRowViewModel> _results = Array.Empty<FileRowViewModel>();

    [ObservableProperty]
    private bool _hasSearched;

    public SearchViewModel(FilesRepository filesRepository, ILogger logger)
    {
        _filesRepository = filesRepository;
        _logger = logger;
    }

    [RelayCommand]
    private void Search()
    {
        _logger.Information("Searching for query: {Query}", Query);
        HasSearched = true;

        if (string.IsNullOrWhiteSpace(Query))
        {
            Results = Array.Empty<FileRowViewModel>();
            return;
        }

        var records = _filesRepository.SearchFts(Query, limit: 200);
        Results = records.Select(r => new FileRowViewModel(r)).ToList();
    }

    [RelayCommand]
    private void OpenFile(FileRowViewModel? row)
    {
        if (row is null) return;
        _logger.Information("Open requested for {FileId}", row.Id);
        // Handled by view code-behind or future command wiring.
    }
}
