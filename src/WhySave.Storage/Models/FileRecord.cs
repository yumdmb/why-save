namespace WhySave.Storage.Models;

public class FileRecord
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string Filename { get; set; } = "";
    public string? Ext { get; set; }
    public long SizeBytes { get; set; }
    public long? VolumeSerial { get; set; }
    public long? NtfsFileId { get; set; }
    public string? Sha256 { get; set; }
    public string Status { get; set; } = "";
    public byte[]? ReasonCipher { get; set; }
    public byte[]? NotesCipher { get; set; }
    public string? Project { get; set; }
    public string? Url { get; set; }
    public string? Referrer { get; set; }
    public string? TabTitle { get; set; }
    public string? ParentFileId { get; set; }
    public long FirstSeenAt { get; set; }
    public long? SavedAt { get; set; }
    public long? LastPromptedAt { get; set; }
    public long? LastResolvedAt { get; set; }
    public long? LastOpenedViaAppAt { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
}
