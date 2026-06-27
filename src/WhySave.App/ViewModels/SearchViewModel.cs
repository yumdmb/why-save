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
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    private string _query = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private IReadOnlyList<FileRowViewModel> _results = Array.Empty<FileRowViewModel>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _hasLoaded;

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

    public bool IsEmpty => HasLoaded && !IsSearching && Results.Count == 0;

    public string EmptyMessage => string.IsNullOrWhiteSpace(Query)
        ? "No saved files yet. Files you add context to or import into Why Save will appear here."
        : "No results. Try a different keyword from the filename, project, URL, reason, or notes.";

    public Task RefreshAsync()
    {
        return string.IsNullOrWhiteSpace(Query)
            ? LoadBrowseResultsAsync()
            : SearchAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(Query))
        {
            await LoadBrowseResultsAsync();
            return;
        }

        HasLoaded = true;
        IsSearching = true;

        try
        {
            _logger.Information("Find search executed for query");
            var results = await _searchService.SearchAsync(Query);
            Results = ToRows(results);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Find search failed");
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

    private async Task LoadBrowseResultsAsync()
    {
        HasLoaded = true;
        IsSearching = true;

        try
        {
            _logger.Information("Find browse loaded");
            var results = await _searchService.BrowseAsync();
            Results = ToRows(results);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Find browse failed");
            Results = Array.Empty<FileRowViewModel>();
        }
        finally
        {
            IsSearching = false;
        }
    }

    private IReadOnlyList<FileRowViewModel> ToRows(IEnumerable<SearchResult> results)
    {
        return results.Select(r => new FileRowViewModel(new Storage.Models.FileRecord
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
}
