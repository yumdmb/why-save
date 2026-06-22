using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Serilog;
using WhySave.Crypto;
using WhySave.Storage.Models;
using WhySave.Storage.Repositories;

namespace WhySave.App.Services;

public sealed class DataManagementService
{
    private readonly FilesRepository _filesRepository;
    private readonly DpapiKeyStore _keyStore;
    private readonly ILogger _logger;

    public DataManagementService(
        FilesRepository filesRepository,
        DpapiKeyStore keyStore,
        ILogger logger)
    {
        _filesRepository = filesRepository;
        _keyStore = keyStore;
        _logger = logger;
    }

    public string GetEncryptionStatus()
    {
        try
        {
            _keyStore.GetOrCreateKey();
            return "Enabled — key sealed with Windows DPAPI (CurrentUser)";
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to read encryption key status");
            return "Unavailable";
        }
    }

    public bool RotateKey(out string message)
    {
        try
        {
            var records = _filesRepository.ListAll().ToList();

            var newKey = RandomNumberGenerator.GetBytes(AesGcmCrypto.KeySize);

            _filesRepository.UpdateCryptoKey(newKey);

            foreach (var record in records)
            {
                if (record.Reason is not null || record.Notes is not null)
                {
                    record.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    _filesRepository.Update(record);
                }
            }

            _keyStore.ReplaceKey(newKey);

            message = $"Key rotated. {records.Count} record(s) re-encrypted.";
            _logger.Information("Encryption key rotated; {Count} records re-encrypted", records.Count);
            return true;
        }
        catch (Exception ex)
        {
            message = "Key rotation failed. See logs for details.";
            _logger.Error(ex, "Encryption key rotation failed");
            return false;
        }
    }

    public bool ExportData(string destinationPath, out string message)
    {
        try
        {
            var records = _filesRepository.ListAll().ToList();
            var export = records.Select(r => new
            {
                r.Id,
                r.Path,
                r.Filename,
                r.Ext,
                r.Status,
                r.Project,
                r.Url,
                r.Reason,
                r.Notes,
                SavedAt = r.SavedAt.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(r.SavedAt.Value).ToString("O")
                    : null,
                FirstSeenAt = DateTimeOffset.FromUnixTimeMilliseconds(r.FirstSeenAt).ToString("O"),
            });

            var json = JsonSerializer.Serialize(export, new JsonSerializerOptions
            {
                WriteIndented = true,
            });

            File.WriteAllText(destinationPath, json);

            message = $"Exported {records.Count} record(s) to {destinationPath}";
            _logger.Information("Data exported to {ExportPath}; {Count} records", destinationPath, records.Count);
            return true;
        }
        catch (Exception ex)
        {
            message = "Export failed. See logs for details.";
            _logger.Error(ex, "Data export failed");
            return false;
        }
    }

    public bool ClearAllData(out string message)
    {
        try
        {
            _filesRepository.ClearAll();
            message = "All file records cleared.";
            _logger.Information("All data cleared by user");
            return true;
        }
        catch (Exception ex)
        {
            message = "Clear data failed. See logs for details.";
            _logger.Error(ex, "Clear data failed");
            return false;
        }
    }
}
