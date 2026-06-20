using System.IO;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using WhySave.Crypto;
using WhySave.Storage.Models;
using WhySave.Storage.Repositories;

namespace WhySave.Tests.Storage;

public class FilesRepositoryEncryptionTests : StorageTestBase, IDisposable
{
    private readonly string _tempDir;
    private readonly byte[] _key;

    public FilesRepositoryEncryptionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WhySaveCryptoTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _key = RandomNumberGenerator.GetBytes(AesGcmCrypto.KeySize);
    }

    public new void Dispose()
    {
        base.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    private FileRecord NewRecord(string id, string status = "contexted") => new()
    {
        Id = id,
        Path = "/p/file.pdf",
        Filename = "file.pdf",
        Ext = ".pdf",
        SizeBytes = 4096,
        Status = status,
        FirstSeenAt = 5000,
        SavedAt = 5000,
        CreatedAt = 1000,
        UpdatedAt = 1000,
    };

    [Fact]
    public void Reason_Is_Encrypted_On_Insert_And_Decrypted_On_Read()
    {
        var repo = new FilesRepository(Connection, _key);
        var record = NewRecord("e1");
        record.Reason = "For the transformers lecture";
        record.Notes = "see slide 12";

        repo.Insert(record);

        var fetched = repo.GetById("e1");
        Assert.NotNull(fetched);
        Assert.Equal("For the transformers lecture", fetched!.Reason);
        Assert.Equal("see slide 12", fetched.Notes);
        Assert.NotNull(fetched.ReasonCipher);
        Assert.NotNull(fetched.NotesCipher);
    }

    [Fact]
    public void Reason_Is_Encrypted_On_Update_And_Decrypted_On_Read()
    {
        var repo = new FilesRepository(Connection, _key);
        repo.Insert(NewRecord("e2"));

        var record = repo.GetById("e2");
        Assert.NotNull(record);
        record!.Reason = "Updated reason text";
        record.Notes = "Updated notes";
        repo.Update(record);

        var fetched = repo.GetById("e2");
        Assert.NotNull(fetched);
        Assert.Equal("Updated reason text", fetched!.Reason);
        Assert.Equal("Updated notes", fetched.Notes);
    }

    [Fact]
    public void Reason_Persists_Encrypted_Across_New_Repo_Instance()
    {
        var repo1 = new FilesRepository(Connection, _key);
        repo1.Insert(NewRecord("e3"));
        var record = repo1.GetById("e3");
        Assert.NotNull(record);
        record!.Reason = "For the transformers lecture";
        record.Notes = "see slide 12";
        repo1.Update(record);

        var repo2 = new FilesRepository(Connection, _key);
        var fetched = repo2.GetById("e3");
        Assert.NotNull(fetched);
        Assert.Equal("For the transformers lecture", fetched!.Reason);
        Assert.Equal("see slide 12", fetched.Notes);
    }

    [Fact]
    public void Plaintext_Reason_Is_Never_Written_To_Database_File_Bytes()
    {
        var reason = "For the transformers lecture";
        var notes = "see slide 12";
        var reasonBytes = Encoding.UTF8.GetBytes(reason);
        var notesBytes = Encoding.UTF8.GetBytes(notes);

        var repo = new FilesRepository(Connection, _key);
        var record = NewRecord("e4");
        record.Reason = reason;
        record.Notes = notes;
        repo.Insert(record);

        var rawRow = Connection.QueryFirstOrDefault<(byte[]? reasonCipher, byte[]? notesCipher)>(
            "SELECT reason_cipher, notes_cipher FROM files WHERE id = 'e4'");

        Assert.NotNull(rawRow.reasonCipher);
        Assert.NotNull(rawRow.notesCipher);

        var dbBytes = ReadDbFileBytes();
        Assert.False(ContainsSequence(dbBytes, reasonBytes),
            "Plaintext reason must not appear in the database file bytes");
        Assert.False(ContainsSequence(dbBytes, notesBytes),
            "Plaintext notes must not appear in the database file bytes");

        Assert.NotEqual(reasonBytes, rawRow.reasonCipher);
        Assert.NotEqual(notesBytes, rawRow.notesCipher);
    }

    [Fact]
    public void Decrypt_With_Wrong_Key_Throws()
    {
        var repo = new FilesRepository(Connection, _key);
        repo.Insert(NewRecord("e5"));
        var record = repo.GetById("e5");
        Assert.NotNull(record);
        record!.Reason = "secret reason";
        repo.Update(record);

        var wrongKey = RandomNumberGenerator.GetBytes(AesGcmCrypto.KeySize);
        var repoWithWrongKey = new FilesRepository(Connection, wrongKey);

        Assert.ThrowsAny<CryptographicException>(() => repoWithWrongKey.GetById("e5"));
    }

    [Fact]
    public void No_Key_Repository_Returns_Raw_Cipher_Blobs()
    {
        var encRepo = new FilesRepository(Connection, _key);
        encRepo.Insert(NewRecord("e6"));
        var record = encRepo.GetById("e6");
        Assert.NotNull(record);
        record!.Reason = "plaintext reason";
        encRepo.Update(record);

        var rawRepo = new FilesRepository(Connection);
        var raw = rawRepo.GetById("e6");
        Assert.NotNull(raw);
        Assert.Null(raw!.Reason);
        Assert.NotNull(raw.ReasonCipher);

        var decrypted = AesGcmCrypto.Decrypt(raw.ReasonCipher!, _key);
        Assert.Equal("plaintext reason", Encoding.UTF8.GetString(decrypted));
    }

    [Fact]
    public void SearchFts_Decrypts_Reason_For_Each_Candidate()
    {
        var repo = new FilesRepository(Connection, _key);
        var record = NewRecord("e7", status: "contexted");
        record.Path = "/p/AI_Paper.pdf";
        record.Filename = "AI_Paper.pdf";
        record.Project = "ML-course";
        record.Reason = "For the transformers lecture";
        repo.Insert(record);

        var results = repo.SearchFts("AI_Paper").ToList();
        Assert.Single(results);
        Assert.Equal("For the transformers lecture", results[0].Reason);
    }

    [Fact]
    public void ListByStatus_Decrypts_Reason_For_Each_Row()
    {
        var repo = new FilesRepository(Connection, _key);
        repo.Insert(NewRecord("e8", status: "contexted"));
        repo.Insert(NewRecord("e9", status: "contexted"));

        var r1 = repo.GetById("e8"); r1!.Reason = "reason eight"; repo.Update(r1);
        var r2 = repo.GetById("e9"); r2!.Reason = "reason nine"; repo.Update(r2);

        var rows = repo.ListByStatus("contexted").ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Reason == "reason eight");
        Assert.Contains(rows, r => r.Reason == "reason nine");
    }

    [Fact]
    public void Clearing_Reason_Sets_Cipher_To_Null()
    {
        var repo = new FilesRepository(Connection, _key);
        repo.Insert(NewRecord("e10"));
        var record = repo.GetById("e10");
        record!.Reason = "temp reason";
        repo.Update(record);

        record.Reason = null;
        record.ReasonCipher = null;
        repo.Update(record);

        var fetched = repo.GetById("e10");
        Assert.NotNull(fetched);
        Assert.Null(fetched!.Reason);
        Assert.Null(fetched.ReasonCipher);
    }

    private byte[] ReadDbFileBytes()
    {
        var walPath = Connection.DataSource + "-wal";
        var mainBytes = ReadAllBytesShared(Connection.DataSource);
        var allBytes = new List<byte>(mainBytes);
        if (File.Exists(walPath))
            allBytes.AddRange(ReadAllBytesShared(walPath));
        return allBytes.ToArray();
    }

    private static byte[] ReadAllBytesShared(string path)
    {
        using var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[fs.Length];
        fs.ReadExactly(bytes, 0, bytes.Length);
        return bytes;
    }

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0) return true;
        if (needle.Length > haystack.Length) return false;

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return true;
        }
        return false;
    }
}
