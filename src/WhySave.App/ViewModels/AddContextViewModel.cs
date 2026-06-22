using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using WhySave.Storage.Models;
using WhySave.Storage.Repositories;

namespace WhySave.App.ViewModels;

public partial class AddContextViewModel : ObservableObject
{
    private readonly FileRecord _record;
    private readonly FilesRepository _filesRepository;
    private readonly ILogger _logger;

    [ObservableProperty]
    private string _reason = "";

    [ObservableProperty]
    private string _project = "";

    [ObservableProperty]
    private string _sourceUrl = "";

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    private DateTime? _savedDate;

    public AddContextViewModel(
        FileRecord record,
        FilesRepository filesRepository,
        ILogger logger,
        IReadOnlyList<string> recentProjects)
    {
        _record = record;
        _filesRepository = filesRepository;
        _logger = logger;
        RecentProjects = recentProjects;

        Reason = record.Reason ?? "";
        Project = record.Project ?? "";
        SourceUrl = record.Url ?? "";
        Notes = record.Notes ?? "";
        SavedDate = record.SavedAt.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(record.SavedAt.Value).DateTime
            : DateTime.Now;
    }

    public string Filename => _record.Filename;

    public string Path => _record.Path;

    public string? Ext => _record.Ext;

    public IReadOnlyList<string> RecentProjects { get; }

    public bool Saved { get; private set; }

    public event EventHandler? RequestClose;

    [RelayCommand]
    private void Save()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _record.Reason = string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim();
        _record.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        _record.Project = string.IsNullOrWhiteSpace(Project) ? null : Project.Trim();
        _record.Url = string.IsNullOrWhiteSpace(SourceUrl) ? null : SourceUrl.Trim();
        _record.SavedAt = SavedDate.HasValue
            ? new DateTimeOffset(SavedDate.Value.Date, TimeZoneInfo.Local.GetUtcOffset(SavedDate.Value.Date)).ToUnixTimeMilliseconds()
            : _record.SavedAt;
        _record.Status = "contexted";
        _record.LastResolvedAt = nowMs;
        _record.UpdatedAt = nowMs;

        _filesRepository.Update(_record);
        Saved = true;

        _logger.Information("Context saved for {FileId}; status is now {Status}", _record.Id, _record.Status);
        OnRequestClose();
    }

    [RelayCommand]
    private void Cancel()
    {
        _logger.Information("Add Context cancelled for {FileId}", _record.Id);
        OnRequestClose();
    }

    private void OnRequestClose() => RequestClose?.Invoke(this, EventArgs.Empty);
}
