using System.IO;
using System.Security.Cryptography;
using Serilog;
using Serilog.Events;
using WhySave.App.Services;
using WhySave.Core;
using WhySave.Crypto;
using WhySave.Storage;
using WhySave.Storage.Models;
using WhySave.Storage.Repositories;

namespace WhySave.Tests.App;

public class LoggingHardeningTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly string _tempDir;
    private readonly string _logFilePath;
    private readonly byte[] _key;
    private readonly FilesRepository _repo;
    private readonly DpapiKeyStore _keyStore;
    private readonly string _knownReason = "For the transformers lecture at 9am on Tuesday";
    private readonly string _knownNotes = "see slide 12 and the handout about attention mechanisms";

    public LoggingHardeningTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WhySaveLogAudit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var dbPath = Path.Combine(_tempDir, "test.db");
        var keyPath = Path.Combine(_tempDir, "key.bin");
        _logFilePath = Path.Combine(_tempDir, "audit.log");

        _connection = SqliteConnectionFactory.Create(dbPath);
        new DatabaseMigrator(_connection).MigrateAsync();

        _key = RandomNumberGenerator.GetBytes(AesGcmCrypto.KeySize);
        _repo = new FilesRepository(_connection, _key);
        _keyStore = new DpapiKeyStore(keyPath);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    private Serilog.Core.Logger CreateFileLogger() =>
        new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.File(_logFilePath, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

    private string ReadLogFileAfterFlush(Serilog.Core.Logger logger)
    {
        logger.Dispose();
        return File.ReadAllText(_logFilePath);
    }

    private void SeedRecordWithReason()
    {
        var record = new FileRecord
        {
            Id = "audit-1",
            Path = "/downloads/AI_Paper.pdf",
            Filename = "AI_Paper.pdf",
            Ext = ".pdf",
            SizeBytes = 4096,
            Status = "contexted",
            Project = "ML-course",
            Url = "https://example.com/long/url/with/secret/path",
            FirstSeenAt = 5000,
            SavedAt = 5000,
            CreatedAt = 1000,
            UpdatedAt = 1000,
            Reason = _knownReason,
            Notes = _knownNotes,
        };
        _repo.Insert(record);
    }

    [Fact]
    public void ExportData_Does_Not_Log_Decrypted_Reason_Or_Url()
    {
        SeedRecordWithReason();

        var logger = CreateFileLogger();
        var dataManagement = new DataManagementService(_repo, _keyStore, logger);

        var exportPath = Path.Combine(_tempDir, "export.json");
        dataManagement.ExportData(exportPath, out _);
        dataManagement.GetEncryptionStatus();

        var logContent = ReadLogFileAfterFlush(logger);
        Assert.DoesNotContain(_knownReason, logContent);
        Assert.DoesNotContain(_knownNotes, logContent);
        Assert.DoesNotContain("secret/path", logContent);
    }

    [Fact]
    public void RotateKey_Does_Not_Log_Decrypted_Reason()
    {
        SeedRecordWithReason();

        var logger = CreateFileLogger();
        var dataManagement = new DataManagementService(_repo, _keyStore, logger);

        dataManagement.RotateKey(out _);

        var logContent = ReadLogFileAfterFlush(logger);
        Assert.DoesNotContain(_knownReason, logContent);
        Assert.DoesNotContain(_knownNotes, logContent);
    }

    [Fact]
    public async Task SearchService_Does_Not_Log_Decrypted_Reason()
    {
        SeedRecordWithReason();

        var logger = CreateFileLogger();

        var searchService = new SearchService(_repo);
        await searchService.SearchAsync("AI_Paper");

        var logContent = ReadLogFileAfterFlush(logger);
        Assert.DoesNotContain(_knownReason, logContent);
        Assert.DoesNotContain(_knownNotes, logContent);
    }

    [Fact]
    public void ClearData_Does_Not_Log_Decrypted_Reason()
    {
        SeedRecordWithReason();

        var logger = CreateFileLogger();
        var dataManagement = new DataManagementService(_repo, _keyStore, logger);

        dataManagement.ClearAllData(out _);

        var logContent = ReadLogFileAfterFlush(logger);
        Assert.DoesNotContain(_knownReason, logContent);
        Assert.DoesNotContain(_knownNotes, logContent);
    }

    [Fact]
    public async Task Full_Workflow_Does_Not_Log_Reason_Or_Url()
    {
        SeedRecordWithReason();

        var logger = CreateFileLogger();
        var dataManagement = new DataManagementService(_repo, _keyStore, logger);
        var searchService = new SearchService(_repo);

        await searchService.SearchAsync("AI_Paper");
        await searchService.SearchAsync("reason:transformers");
        dataManagement.GetEncryptionStatus();
        dataManagement.RotateKey(out _);

        var exportPath = Path.Combine(_tempDir, "full-export.json");
        dataManagement.ExportData(exportPath, out _);

        var logContent = ReadLogFileAfterFlush(logger);
        Assert.DoesNotContain(_knownReason, logContent);
        Assert.DoesNotContain(_knownNotes, logContent);
        Assert.DoesNotContain("secret/path", logContent);
    }
}
