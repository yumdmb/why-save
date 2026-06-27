using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhySave.Storage.Models;

namespace WhySave.App.ViewModels;

public partial class FileRowViewModel : ObservableObject
{
    private readonly FileRecord _record;

    public FileRowViewModel(FileRecord record)
    {
        _record = record;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddWhy))]
    private IRelayCommand? _addWhyCommand;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    private IRelayCommand? _openCommand;

    [ObservableProperty]
    private IRelayCommand? _editContextCommand;

    public bool CanAddWhy => AddWhyCommand is not null;

    public bool CanOpen => OpenCommand is not null;

    public string Id => _record.Id;

    public string Filename => _record.Filename;

    public string Path => _record.Path;

    public string? Project => _record.Project;

    public string Status => _record.Status;

    public string? ReasonSnippet => _record.Reason is null || _record.Reason.Length <= 120
        ? _record.Reason
        : _record.Reason.Substring(0, 120) + "…";

    public DateTimeOffset? SavedAt => _record.SavedAt.HasValue
        ? DateTimeOffset.FromUnixTimeMilliseconds(_record.SavedAt.Value)
        : null;

    public string SavedAtDisplay => SavedAt?.ToLocalTime().ToString("g") ?? "—";

    public string StatusBadge => Status switch
    {
        "pending" => "Pending",
        "contexted" => "Has context",
        "legacy" => "Imported",
        "missing" => "Missing",
        _ => Status,
    };
}
