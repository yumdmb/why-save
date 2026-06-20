using System.Security.Cryptography;
using WhySave.Native;
using WhySave.Storage.Repositories;

namespace WhySave.Core;

public sealed class IdentityResolver : IIdentityResolver
{
    public const long MaxHashableSizeBytes = 50 * 1024 * 1024;

    private readonly FilesRepository _filesRepo;
    private readonly Func<string, FileNtfsIdentity?> _ntfsProvider;

    public IdentityResolver(FilesRepository filesRepo)
        : this(filesRepo, NativeFileIdentity.GetFileIdentity)
    {
    }

    public IdentityResolver(FilesRepository filesRepo, Func<string, FileNtfsIdentity?> ntfsProvider)
    {
        _filesRepo = filesRepo;
        _ntfsProvider = ntfsProvider;
    }

    public Task<FileIdentity> ResolveAsync(string path, CancellationToken ct = default)
    {
        var filename = Path.GetFileName(path);
        var ext = Path.GetExtension(path);

        if (!File.Exists(path))
        {
            return Task.FromResult(new FileIdentity(
                ExistingFileId: null,
                SizeBytes: 0,
                Filename: filename,
                Ext: ext,
                VolumeSerial: null,
                NtfsFileId: null,
                Sha256: null));
        }

        var info = new FileInfo(path);
        var sizeBytes = info.Length;

        long? volumeSerial = null;
        long? ntfsFileId = null;

        var ntfs = _ntfsProvider(path);
        if (ntfs is { } nt)
        {
            volumeSerial = nt.VolumeSerial;
            ntfsFileId = nt.NtfsFileId;
        }

        if (volumeSerial is not null && ntfsFileId is not null)
        {
            var byNtfs = _filesRepo.GetByNtfsId(volumeSerial.Value, ntfsFileId.Value);
            if (byNtfs is not null)
            {
                return Task.FromResult(new FileIdentity(
                    ExistingFileId: byNtfs.Id,
                    SizeBytes: sizeBytes,
                    Filename: filename,
                    Ext: ext,
                    VolumeSerial: volumeSerial,
                    NtfsFileId: ntfsFileId,
                    Sha256: byNtfs.Sha256));
            }
        }

        var byPath = _filesRepo.GetByPath(path);
        if (byPath is not null)
        {
            return Task.FromResult(new FileIdentity(
                ExistingFileId: byPath.Id,
                SizeBytes: sizeBytes,
                Filename: filename,
                Ext: ext,
                VolumeSerial: volumeSerial,
                NtfsFileId: ntfsFileId,
                Sha256: byPath.Sha256));
        }

        string? sha256 = null;
        if (sizeBytes < MaxHashableSizeBytes)
        {
            sha256 = ComputeSha256(path);
            if (sha256 is not null)
            {
                var byHash = _filesRepo.GetBySha256(sha256);
                if (byHash is not null)
                {
                    return Task.FromResult(new FileIdentity(
                        ExistingFileId: byHash.Id,
                        SizeBytes: sizeBytes,
                        Filename: filename,
                        Ext: ext,
                        VolumeSerial: volumeSerial,
                        NtfsFileId: ntfsFileId,
                        Sha256: sha256));
                }
            }
        }

        return Task.FromResult(new FileIdentity(
            ExistingFileId: null,
            SizeBytes: sizeBytes,
            Filename: filename,
            Ext: ext,
            VolumeSerial: volumeSerial,
            NtfsFileId: ntfsFileId,
            Sha256: sha256));
    }

    private static string? ComputeSha256(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
