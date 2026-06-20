using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using WhySave.Crypto;
using WhySave.Storage.Models;

namespace WhySave.Storage.Repositories;

public class FilesRepository
{
    private readonly SqliteConnection _connection;
    private readonly byte[]? _cryptoKey;

    static FilesRepository()
    {
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public FilesRepository(SqliteConnection connection) : this(connection, cryptoKey: null) { }

    public FilesRepository(SqliteConnection connection, byte[]? cryptoKey)
    {
        _connection = connection;
        _cryptoKey = cryptoKey;
    }

    public void Insert(FileRecord record)
    {
        EncryptFields(record);
        _connection.Execute(
            """
            INSERT INTO files (
                id, path, filename, ext, size_bytes, volume_serial, ntfs_file_id,
                sha256, status, reason_cipher, notes_cipher, project, url, referrer,
                tab_title, parent_file_id, first_seen_at, saved_at, last_prompted_at,
                last_resolved_at, last_opened_via_app_at, created_at, updated_at
            ) VALUES (
                @Id, @Path, @Filename, @Ext, @SizeBytes, @VolumeSerial, @NtfsFileId,
                @Sha256, @Status, @ReasonCipher, @NotesCipher, @Project, @Url, @Referrer,
                @TabTitle, @ParentFileId, @FirstSeenAt, @SavedAt, @LastPromptedAt,
                @LastResolvedAt, @LastOpenedViaAppAt, @CreatedAt, @UpdatedAt
            )
            """,
            record);
    }

    public void Update(FileRecord record)
    {
        EncryptFields(record);
        _connection.Execute(
            """
            UPDATE files SET
                path = @Path, filename = @Filename, ext = @Ext, size_bytes = @SizeBytes,
                volume_serial = @VolumeSerial, ntfs_file_id = @NtfsFileId, sha256 = @Sha256,
                status = @Status, prior_status = @PriorStatus,
                reason_cipher = @ReasonCipher, notes_cipher = @NotesCipher,
                project = @Project, url = @Url, referrer = @Referrer, tab_title = @TabTitle,
                parent_file_id = @ParentFileId, first_seen_at = @FirstSeenAt,
                saved_at = @SavedAt, last_prompted_at = @LastPromptedAt,
                last_resolved_at = @LastResolvedAt,
                last_opened_via_app_at = @LastOpenedViaAppAt, updated_at = @UpdatedAt
            WHERE id = @Id
            """,
            record);
    }

    public void MarkMissing(string id)
    {
        _connection.Execute(
            """
            UPDATE files SET
                prior_status = status,
                status = 'missing',
                updated_at = @now
            WHERE id = @id AND status != 'missing'
            """,
            new { id, now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
    }

    public FileRecord? GetById(string id) =>
        DecryptFields(_connection.QueryFirstOrDefault<FileRecord>(
            "SELECT * FROM files WHERE id = @id", new { id }));

    public FileRecord? GetByPath(string path) =>
        DecryptFields(_connection.QueryFirstOrDefault<FileRecord>(
            "SELECT * FROM files WHERE path = @path", new { path }));

    public FileRecord? GetByNtfsId(long volumeSerial, long ntfsFileId) =>
        DecryptFields(_connection.QueryFirstOrDefault<FileRecord>(
            "SELECT * FROM files WHERE volume_serial = @volumeSerial AND ntfs_file_id = @ntfsFileId",
            new { volumeSerial, ntfsFileId }));

    public FileRecord? GetBySha256(string sha256) =>
        DecryptFields(_connection.QueryFirstOrDefault<FileRecord>(
            "SELECT * FROM files WHERE sha256 = @sha256", new { sha256 }));

    public IEnumerable<FileRecord> ListByStatus(string status) =>
        DecryptFields(_connection.Query<FileRecord>(
            "SELECT * FROM files WHERE status = @status ORDER BY first_seen_at DESC",
            new { status }));

    public IEnumerable<FileRecord> SearchFts(string query, int limit = 200)
    {
        var ftsQuery = "\"" + query.Replace("\"", "\"\"") + "\"";
        var results = _connection.Query<FileRecord>(
            """
            SELECT f.*
            FROM files_fts
            JOIN files f ON f.rowid = files_fts.rowid
            WHERE files_fts MATCH @query
            ORDER BY files_fts.rank
            LIMIT @limit
            """,
            new { query = ftsQuery, limit });
        return DecryptFields(results);
    }

    private void EncryptFields(FileRecord record)
    {
        if (_cryptoKey is null)
            return;

        if (record.Reason is not null)
            record.ReasonCipher = AesGcmCrypto.Encrypt(Encoding.UTF8.GetBytes(record.Reason), _cryptoKey);
        if (record.Notes is not null)
            record.NotesCipher = AesGcmCrypto.Encrypt(Encoding.UTF8.GetBytes(record.Notes), _cryptoKey);
    }

    private FileRecord? DecryptFields(FileRecord? record)
    {
        if (_cryptoKey is null || record is null)
            return record;

        record.Reason = record.ReasonCipher is not null
            ? Encoding.UTF8.GetString(AesGcmCrypto.Decrypt(record.ReasonCipher, _cryptoKey))
            : null;
        record.Notes = record.NotesCipher is not null
            ? Encoding.UTF8.GetString(AesGcmCrypto.Decrypt(record.NotesCipher, _cryptoKey))
            : null;
        return record;
    }

    private IEnumerable<FileRecord> DecryptFields(IEnumerable<FileRecord> records)
    {
        foreach (var r in records)
            DecryptFields(r);
        return records;
    }
}
