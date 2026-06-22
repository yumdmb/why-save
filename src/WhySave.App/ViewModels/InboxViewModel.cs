using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using WhySave.Storage.Models;
using WhySave.Storage.Repositories;

namespace WhySave.App.ViewModels;

public partial class InboxViewModel : ObservableObject
{
    private readonly FilesRepository _filesRepository;
    private readonly ILogger _logger;

    [ObservableProperty]
    private ObservableCollection<FileRowViewModel> _items = new();

    [ObservableProperty]
    private ObservableCollection<FileRowViewModel> _selectedItems = new();

    public InboxViewModel(FilesRepository filesRepository, ILogger logger)
    {
        _filesRepository = filesRepository;
        _logger = logger;
    }

    public int PendingCount => Items.Count;

    public void Refresh()
    {
        Items.Clear();
        foreach (var record in _filesRepository.ListByStatus("pending"))
        {
            Items.Add(new FileRowViewModel(record));
        }

        OnPropertyChanged(nameof(PendingCount));
        _logger.Information("Inbox refreshed; {Count} pending items", Items.Count);
    }

    [RelayCommand]
    private void AddWhy(FileRowViewModel? row)
    {
        if (row is null) return;
        _logger.Information("Add why requested for {FileId}", row.Id);
        // Will open Add Context form in later milestone.
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
