using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using WhySave.App.Services;
using WhySave.Core;
using WhySave.Storage.Repositories;

namespace WhySave.App.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly SearchService _searchService;
    private readonly FilesRepository _filesRepository;
    private readonly IAddContextDialogService _dialogService;
    private readonly ILogger _logger;

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private IReadOnlyList<FileRowViewModel> _results = Array.Empty<FileRowViewModel>();

    [ObservableProperty]
    private bool _hasSearched;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isSearching;

    public SearchViewModel(
        SearchService searchService,
        FilesRepository filesRepository,
        IAddContextDialogService dialogService,
        ILogger logger)
    {
        _searchService = searchService;
        _filesRepository = filesRepository;
        _dialogService = dialogService;
        _logger = logger;
    }

    public bool IsEmpty => HasSearched && !IsSearching && Results.Count == 0;

    [RelayCommand]
    private async Task SearchAsync()
    {
        HasSearched = true;
        IsSearching = true;

        if (string.IsNullOrWhiteSpace(Query))
        {
            Results = Array.Empty<FileRowViewModel>();
            IsSearching = false;
            return;
        }

        try
        {
            _logger.Information("Search executed for query");
            var results = await _searchService.SearchAsync(Query);
            Results = results.Select(r => new FileRowViewModel(new Storage.Models.FileRecord
            {
                Id = r.FileId,
                Path = r.Path,
                Filename = r.Filename,
                Project = r.Project,
                Status = r.Status,
                SavedAt = r.SavedAt?.ToUnixTimeMilliseconds(),
                Reason = r.ReasonSnippet,
            })
            {
                OpenCommand = OpenFileCommand,
                EditContextCommand = EditContextCommand,
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Search failed");
            Results = Array.Empty<FileRowViewModel>();
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void OpenFile(FileRowViewModel? row)
    {
        if (row is null) return;

        try
        {
            if (!File.Exists(row.Path))
            {
                _logger.Warning("Cannot open file; not found on disk: {FileId}", row.Id);
                return;
            }

            Process.Start(new ProcessStartInfo(row.Path) { UseShellExecute = true });
            _filesRepository.MarkOpenedViaApp(row.Id);
            _logger.Information("Opened file via OS handler: {FileId}", row.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open file: {FileId}", row.Id);
        }
    }

    [RelayCommand]
    private void EditContext(FileRowViewModel? row)
    {
        if (row is null) return;
        _logger.Information("Edit context requested from search: {FileId}", row.Id);
        _dialogService.ShowAddContext(row.Id);
    }
}
