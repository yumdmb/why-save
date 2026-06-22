using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using WhySave.App.Services;
using WhySave.Core;
using WhySave.Crypto;
using WhySave.Storage;
using WhySave.Storage.Models;
using WhySave.Storage.Repositories;

namespace WhySave.Tests.App;

public class DataManagementServiceTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly string _tempPath;
    private readonly string _tempDir;
    private readonly byte[] _key;
    private readonly FilesRepository _repo;
    private readonly DpapiKeyStore _keyStore;
    private readonly DataManagementService _dataManagement;
    private readonly string _keyFilePath;

    public DataManagementServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WhySaveDataMgmt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _tempPath = Path.Combine(_tempDir, "test.db");
        _keyFilePath = Path.Combine(_tempDir, "key.bin");

        _connection = SqliteConnectionFactory.Create(_tempPath);
        new DatabaseMigrator(_connection).MigrateAsync();

        _key = RandomNumberGenerator.GetBytes(AesGcmCrypto.KeySize);
        _repo = new FilesRepository(_connection, _key);
        _keyStore = new DpapiKeyStore(_keyFilePath);

        var logger = new Serilog.LoggerConfiguration().CreateLogger();
        _dataManagement = new DataManagementService(_repo, _keyStore, logger);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    private static FileRecord NewRecord(string id, string? reason = null, string? notes = null) => new()
    {
        Id = id,
        Path = $"/p/{id}.pdf",
        Filename = $"{id}.pdf",
        Ext = ".pdf",
        SizeBytes = 4096,
        Status = "contexted",
        FirstSeenAt = 5000,
        SavedAt = 5000,
        CreatedAt = 1000,
        UpdatedAt = 1000,
        Reason = reason,
        Notes = notes,
    };

    [Fact]
    public void GetEncryptionStatus_Returns_Enabled_When_Key_Exists()
    {
        var status = _dataManagement.GetEncryptionStatus();
        Assert.Contains("Enabled", status);
    }

    [Fact]
    public void RotateKey_ReEncrypts_All_Records()
    {
        _repo.Insert(NewRecord("r1", reason: "reason one", notes: "notes one"));
        _repo.Insert(NewRecord("r2", reason: "reason two"));

        var oldCipher1 = _repo.GetById("r1")!.ReasonCipher;
        Assert.NotNull(oldCipher1);

        var success = _dataManagement.RotateKey(out var message);
        Assert.True(success);
        Assert.Contains("re-encrypted", message);

        var fetched1 = _repo.GetById("r1");
        Assert.NotNull(fetched1);
        Assert.Equal("reason one", fetched1!.Reason);
        Assert.Equal("notes one", fetched1.Notes);

        var fetched2 = _repo.GetById("r2");
        Assert.NotNull(fetched2);
        Assert.Equal("reason two", fetched2!.Reason);

        Assert.NotEqual(oldCipher1, fetched1.ReasonCipher);
    }

    [Fact]
    public void RotateKey_Persists_New_Key_To_DPAPI_File()
    {
        _repo.Insert(NewRecord("r1", reason: "secret reason"));

        _dataManagement.RotateKey(out _);

        Assert.True(File.Exists(_keyFilePath));

        var sealedBytes = File.ReadAllBytes(_keyFilePath);
        var unsealed = ProtectedData.Unprotect(sealedBytes, null, DataProtectionScope.CurrentUser);
        Assert.Equal(AesGcmCrypto.KeySize, unsealed.Length);
    }

    [Fact]
    public void ExportData_Writes_Json_With_All_Records()
    {
        _repo.Insert(NewRecord("e1", reason: "reason one", notes: "notes one"));
        _repo.Insert(NewRecord("e2", reason: "reason two"));

        var exportPath = Path.Combine(_tempDir, "export.json");

        var success = _dataManagement.ExportData(exportPath, out var message);
        Assert.True(success);
        Assert.True(File.Exists(exportPath));

        var json = File.ReadAllText(exportPath);
        var docs = JsonDocument.Parse(json);
        Assert.Equal(2, docs.RootElement.GetArrayLength());
    }

    [Fact]
    public void ExportData_Includes_Decrypted_Reason()
    {
        _repo.Insert(NewRecord("e3", reason: "For the transformers lecture"));

        var exportPath = Path.Combine(_tempDir, "export-with-reason.json");

        _dataManagement.ExportData(exportPath, out _);

        var json = File.ReadAllText(exportPath);
        Assert.Contains("For the transformers lecture", json);
    }

    [Fact]
    public void ClearAllData_Removes_All_Records()
    {
        _repo.Insert(NewRecord("c1", reason: "reason one"));
        _repo.Insert(NewRecord("c2", reason: "reason two"));
        _repo.Insert(NewRecord("c3", reason: "reason three"));

        Assert.Equal(3, _repo.ListAll().Count());

        var success = _dataManagement.ClearAllData(out var message);
        Assert.True(success);
        Assert.Contains("cleared", message);

        Assert.Empty(_repo.ListAll());
    }

    [Fact]
    public void RotateKey_With_No_Records_Still_Succeeds()
    {
        var success = _dataManagement.RotateKey(out var message);
        Assert.True(success);
        Assert.Contains("0 record", message);
    }
}
