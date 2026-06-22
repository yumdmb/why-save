using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using WhySave.App.Services;
using WhySave.Storage.Models;
using WhySave.Storage.Repositories;

namespace WhySave.App.ViewModels;

public partial class InboxViewModel : ObservableObject
{
    private readonly FilesRepository _filesRepository;
    private readonly IAddContextDialogService _dialogService;
    private readonly ILogger _logger;

    [ObservableProperty]
    private ObservableCollection<FileRowViewModel> _items = new();

    [ObservableProperty]
    private ObservableCollection<FileRowViewModel> _selectedItems = new();

    [ObservableProperty]
    private bool _isEmpty;

    public InboxViewModel(
        FilesRepository filesRepository,
        IAddContextDialogService dialogService,
        ILogger logger)
    {
        _filesRepository = filesRepository;
        _dialogService = dialogService;
        _logger = logger;
        IsEmpty = true;
    }

    public int PendingCount => Items.Count;

    public void Refresh()
    {
        Items.Clear();
        SelectedItems.Clear();
        foreach (var record in _filesRepository.ListByStatus("pending"))
        {
            var row = new FileRowViewModel(record)
            {
                AddWhyCommand = AddWhyCommand,
            };
            Items.Add(row);
        }

        IsEmpty = Items.Count == 0;
        OnPropertyChanged(nameof(PendingCount));
        _logger.Information("Inbox refreshed; {Count} pending items", Items.Count);
    }

    [RelayCommand]
    private void AddWhy(FileRowViewModel? row)
    {
        if (row is null) return;
        _logger.Information("Add why requested for {FileId}", row.Id);
        _dialogService.ShowAddContext(row.Id);
    }

    [RelayCommand]
    private void DismissSelectedToLegacy()
    {
        var ids = SelectedItems.Select(i => i.Id).ToList();
        if (ids.Count == 0) return;

        foreach (var id in ids)
        {
            var record = _filesRepository.GetById(id);
            if (record is null) continue;
            record.Status = "legacy";
            record.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _filesRepository.Update(record);
        }

        _logger.Information("Dismissed {Count} inbox items to legacy", ids.Count);
        Refresh();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        var ids = SelectedItems.Select(i => i.Id).ToList();
        if (ids.Count == 0) return;

        foreach (var id in ids)
        {
            _filesRepository.Delete(id);
        }

        _logger.Information("Deleted {Count} inbox records", ids.Count);
        Refresh();
    }
}
