using Serilog;
using WhySave.Storage.Repositories;

namespace WhySave.App.Services;

public sealed class ToastService
{
    public static readonly TimeSpan DebouncePeriod = TimeSpan.FromMinutes(10);

    private readonly ILogger _logger;
    private readonly FilesRepository _filesRepository;
    private readonly IAddContextDialogService _dialogService;
    private readonly IToastPresenter _presenter;

    public ToastService(
        ILogger logger,
        FilesRepository filesRepository,
        IAddContextDialogService dialogService,
        IToastPresenter? presenter = null)
    {
        _logger = logger;
        _filesRepository = filesRepository;
        _dialogService = dialogService;
        _presenter = presenter ?? new InAppToastPresenter();
    }

    public void ShowPendingToast(string fileId)
    {
        var record = _filesRepository.GetById(fileId);
        if (record is null)
        {
            _logger.Warning("Cannot show toast for unknown file {FileId}", fileId);
            return;
        }

        if (record.Status != "pending")
        {
            _logger.Debug("Skipping toast for file {FileId} with status {Status}", fileId, record.Status);
            return;
        }

        if (record.LastPromptedAt.HasValue)
        {
            var elapsed = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(record.LastPromptedAt.Value);
            if (elapsed < DebouncePeriod)
            {
                _logger.Debug("Toast debounced for {FileId}; last prompted {Elapsed} ago", fileId, elapsed);
                return;
            }
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        record.LastPromptedAt = nowMs;
        record.UpdatedAt = nowMs;
        _filesRepository.Update(record);

        _logger.Information("Showing pending toast for {FileId} ({Filename})", fileId, record.Filename);

        try
        {
            _presenter.Show(
                record,
                onAddContext: () => OnAddContext(fileId),
                onLater: () => _logger.Debug("Toast dismissed (later) for {FileId}", fileId));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to show toast for {FileId}", fileId);
        }
    }

    private void OnAddContext(string fileId)
    {
        _logger.Information("Add Context chosen from toast for {FileId}", fileId);
        _dialogService.ShowAddContext(fileId);
    }
}
