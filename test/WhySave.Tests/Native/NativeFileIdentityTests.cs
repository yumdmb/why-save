using System.IO;
using WhySave.Native;

namespace WhySave.Tests.Native;

public class NativeFileIdentityTests : IDisposable
{
    private readonly string _tempDir;

    public NativeFileIdentityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WhySaveNativeTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void GetFileIdentity_Returns_NonNull_For_Local_Temp_File()
    {
        var path = Path.Combine(_tempDir, "local.txt");
        File.WriteAllText(path, "hello");

        var identity = NativeFileIdentity.GetFileIdentity(path);

        Assert.NotNull(identity);
        Assert.True(identity!.VolumeSerial != 0, "Volume serial should be non-zero on a local NTFS volume");
        Assert.True(identity.NtfsFileId != 0, "NTFS file ID should be non-zero on a local NTFS volume");
    }

    [Fact]
    public void GetFileIdentity_Returns_Same_Identity_After_Rename_On_Same_Volume()
    {
        var path = Path.Combine(_tempDir, "before.txt");
        var renamed = Path.Combine(_tempDir, "after.txt");
        File.WriteAllText(path, "rename me");

        var before = NativeFileIdentity.GetFileIdentity(path);
        Assert.NotNull(before);

        File.Move(path, renamed);

        var after = NativeFileIdentity.GetFileIdentity(renamed);
        Assert.NotNull(after);

        Assert.Equal(before!.VolumeSerial, after!.VolumeSerial);
        Assert.Equal(before.NtfsFileId, after.NtfsFileId);
    }

    [Fact]
    public void GetFileIdentity_Stable_Across_Content_Edits()
    {
        var path = Path.Combine(_tempDir, "stable.txt");
        File.WriteAllText(path, "v1");

        var before = NativeFileIdentity.GetFileIdentity(path);
        Assert.NotNull(before);

        File.WriteAllText(path, "v2 with more content");

        var after = NativeFileIdentity.GetFileIdentity(path);
        Assert.NotNull(after);

        Assert.Equal(before!.VolumeSerial, after!.VolumeSerial);
        Assert.Equal(before.NtfsFileId, after!.NtfsFileId);
    }

    [Fact]
    public void GetFileIdentity_Returns_Null_For_Nonexistent_File()
    {
        var path = Path.Combine(_tempDir, "does-not-exist-" + Guid.NewGuid() + ".txt");
        Assert.Null(NativeFileIdentity.GetFileIdentity(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void GetFileIdentity_Returns_Null_For_Null_Or_Empty_Path(string? path)
    {
        Assert.Null(NativeFileIdentity.GetFileIdentity(path!));
    }

    [Fact]
    public void GetFileIdentity_For_Directory_Returns_Null_Or_Volume_Only()
    {
        var dirIdentity = NativeFileIdentity.GetFileIdentity(_tempDir);
        Assert.True(dirIdentity is null || dirIdentity.VolumeSerial != 0);
    }
}
