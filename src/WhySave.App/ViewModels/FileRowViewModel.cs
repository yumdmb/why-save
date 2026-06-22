using CommunityToolkit.Mvvm.ComponentModel;
using WhySave.Storage.Models;

namespace WhySave.App.ViewModels;

public partial class FileRowViewModel : ObservableObject
{
    private readonly FileRecord _record;

    public FileRowViewModel(FileRecord record)
    {
        _record = record;
    }

    public string Id => _record.Id;

    public string Filename => _record.Filename;

    public string? Project => _record.Project;

    public string Status => _record.Status;

    public DateTimeOffset? SavedAt => _record.SavedAt.HasValue
        ? DateTimeOffset.FromUnixTimeMilliseconds(_record.SavedAt.Value)
        : null;

    public string SavedAtDisplay => SavedAt?.ToLocalTime().ToString("g") ?? "—";

    public string StatusBadge => Status switch
    {
        "pending" => "Pending",
        "contexted" => "Contexted",
        "legacy" => "Legacy",
        "missing" => "Missing",
        _ => Status,
    };
}
