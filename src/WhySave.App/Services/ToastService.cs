using Microsoft.Toolkit.Uwp.Notifications;
using Serilog;
using WhySave.Storage.Repositories;

namespace WhySave.App.Services;

public sealed class ToastService : IDisposable
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
        _presenter = presenter ?? new ToolkitToastPresenter();
    }

    public void Initialize()
    {
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;
    }

    public void Dispose()
    {
        ToastNotificationManagerCompat.OnActivated -= OnToastActivated;
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

        var arguments = $"action=addContext&fileId={Uri.EscapeDataString(fileId)}";
        var laterArguments = $"action=later&fileId={Uri.EscapeDataString(fileId)}";

        try
        {
            _presenter.Show(record, arguments, laterArguments);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to show toast for {FileId}", fileId);
        }
    }

    public void HandleActivation(ToastNotificationActivatedEventArgsCompat e)
    {
        if (string.IsNullOrEmpty(e.Argument))
            return;

        var args = ToastArguments.Parse(e.Argument);
        if (!args.TryGetValue("action", out var action) ||
            !args.TryGetValue("fileId", out var fileId) ||
            string.IsNullOrEmpty(fileId))
        {
            return;
        }

        _logger.Information("Toast activated: {Action} for {FileId}", action, fileId);

        var record = _filesRepository.GetById(fileId);
        if (record is null)
            return;

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        record.LastPromptedAt = nowMs;
        record.UpdatedAt = nowMs;
        _filesRepository.Update(record);

        if (action == "addContext")
        {
            _dialogService.ShowAddContext(fileId);
        }
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        try
        {
            HandleActivation(e);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling toast activation");
        }
    }
}
