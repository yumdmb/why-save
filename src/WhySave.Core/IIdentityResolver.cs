namespace WhySave.Core;

public record FileIdentity(
    string? ExistingFileId,
    long SizeBytes,
    string Filename,
    string? Ext,
    long? VolumeSerial,
    long? NtfsFileId,
    string? Sha256);

public interface IIdentityResolver
{
    Task<FileIdentity> ResolveAsync(string path, CancellationToken ct = default);
}
