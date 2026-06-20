using System.IO;
using WhySave.Core;

namespace WhySave.Tests.Core;

public class JunkFilterTests : IDisposable
{
    private readonly string _tempDir;

    public JunkFilterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WhySaveJunkTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    private string TempFile(string name, long size = 2048)
    {
        var path = Path.Combine(_tempDir, name);
        using var fs = File.Create(path);
        fs.SetLength(size);
        return path;
    }

    [Fact]
    public void Chrome_Partial_Download_Is_Junk()
    {
        var filter = new JunkFilter();
        var path = TempFile("report.crdownload", size: 4096);

        Assert.True(filter.IsJunk(path, 4096));
    }

    [Fact]
    public void Tmp_File_Is_Junk()
    {
        var filter = new JunkFilter();
        var path = TempFile("download.tmp", size: 4096);

        Assert.True(filter.IsJunk(path, 4096));
    }

    [Fact]
    public void Part_File_Is_Junk()
    {
        var filter = new JunkFilter();
        var path = TempFile("video.part", size: 4096);

        Assert.True(filter.IsJunk(path, 4096));
    }

    [Fact]
    public void Word_Temp_File_Is_Junk()
    {
        var filter = new JunkFilter();
        var path = TempFile("~$report.docx", size: 4096);

        Assert.True(filter.IsJunk(path, 4096));
    }

    [Fact]
    public void Bak_File_Is_Junk()
    {
        var filter = new JunkFilter();
        var path = TempFile("config.bak", size: 4096);

        Assert.True(filter.IsJunk(path, 4096));
    }

    [Fact]
    public void Thumbs_Db_Is_Junk()
    {
        var filter = new JunkFilter();
        var path = TempFile("Thumbs.db", size: 4096);

        Assert.True(filter.IsJunk(path, 4096));
    }

    [Fact]
    public void Desktop_Ini_Is_Junk()
    {
        var filter = new JunkFilter();
        var path = TempFile("desktop.ini", size: 4096);

        Assert.True(filter.IsJunk(path, 4096));
    }

    [Fact]
    public void Shortcut_Is_Junk()
    {
        var filter = new JunkFilter();
        var path = TempFile("shortcut.lnk", size: 4096);

        Assert.True(filter.IsJunk(path, 4096));
    }

    [Fact]
    public void Log_File_Is_Junk()
    {
        var filter = new JunkFilter();
        var path = TempFile("setup.log", size: 4096);

        Assert.True(filter.IsJunk(path, 4096));
    }

    [Fact]
    public void Small_Log_File_Is_Junk_By_Size_And_Glob()
    {
        var filter = new JunkFilter();
        var path = TempFile("setup.log", size: 200);

        Assert.True(filter.IsJunk(path, 200));
    }

    [Fact]
    public void File_Below_Min_Size_Is_Junk()
    {
        var filter = new JunkFilter();
        var path = TempFile("small.pdf", size: 500);

        Assert.True(filter.IsJunk(path, 500));
    }

    [Fact]
    public void Normal_Pdf_Passes()
    {
        var filter = new JunkFilter();
        var path = TempFile("report.pdf", size: 4096);

        Assert.False(filter.IsJunk(path, 4096));
    }

    [Fact]
    public void Large_Document_Passes()
    {
        var filter = new JunkFilter();
        var path = TempFile("big.docx", size: 100_000);

        Assert.False(filter.IsJunk(path, 100_000));
    }

    [Fact]
    public void Allow_Listed_Exe_Passes_Even_If_Updater_Pattern_Would_Block()
    {
        var filter = new JunkFilter(new JunkFilterRules
        {
            AllowGlobs = new[] { "*.exe" },
        });

        var path = TempFile("Installer.exe", size: 4096);

        Assert.False(filter.IsJunk(path, 4096));
    }

    [Fact]
    public void Allow_Overrides_Block_Glob()
    {
        var filter = new JunkFilter(new JunkFilterRules
        {
            BlockGlobs = JunkFilter.DefaultBlockGlobs.Concat(new[] { "*.pdf" }).ToList(),
            AllowGlobs = new[] { "important.pdf" },
        });

        var blocked = TempFile("report.pdf", size: 4096);
        var allowed = TempFile("important.pdf", size: 4096);

        Assert.True(filter.IsJunk(blocked, 4096));
        Assert.False(filter.IsJunk(allowed, 4096));
    }

    [Fact]
    public void User_Added_Zip_Block_Drops_Zips()
    {
        var filter = new JunkFilter(new JunkFilterRules
        {
            BlockGlobs = JunkFilter.DefaultBlockGlobs.Append("*.zip").ToList(),
        });

        var path = TempFile("archive.zip", size: 4096);

        Assert.True(filter.IsJunk(path, 4096));
    }

    [Fact]
    public void User_Added_Zip_Block_Does_Not_Affect_Pdf()
    {
        var filter = new JunkFilter(new JunkFilterRules
        {
            BlockGlobs = JunkFilter.DefaultBlockGlobs.Append("*.zip").ToList(),
        });

        var path = TempFile("report.pdf", size: 4096);

        Assert.False(filter.IsJunk(path, 4096));
    }

    [Fact]
    public void Chrome_Setup_Is_Junk()
    {
        var filter = new JunkFilter();
        var path = TempFile("ChromeSetup.exe", size: 4096);

        Assert.True(filter.IsJunk(path, 4096));
    }

    [Fact]
    public void Chrome_Installer_Is_Junk()
    {
        var filter = new JunkFilter();
        var path = TempFile("chrome_installer.exe", size: 4096);

        Assert.True(filter.IsJunk(path, 4096));
    }

    [Fact]
    public void Updater_Tmp_Is_Junk()
    {
        var filter = new JunkFilter();
        var path = TempFile("update.tmp", size: 4096);

        Assert.True(filter.IsJunk(path, 4096));
    }

    [Fact]
    public void Custom_Min_Size_Affects_Filtering()
    {
        var filter = new JunkFilter(new JunkFilterRules
        {
            MinSizeBytes = 10_000,
        });

        var smallPath = TempFile("report.pdf", size: 5000);
        var bigPath = TempFile("big.pdf", size: 15_000);

        Assert.True(filter.IsJunk(smallPath, 5000));
        Assert.False(filter.IsJunk(bigPath, 15_000));
    }

    [Fact]
    public void UpdateRules_Recompiles_Predicate()
    {
        var filter = new JunkFilter();
        var path = TempFile("archive.zip", size: 4096);

        Assert.False(filter.IsJunk(path, 4096));

        filter.UpdateRules(new JunkFilterRules
        {
            BlockGlobs = JunkFilter.DefaultBlockGlobs.Append("*.zip").ToList(),
        });

        Assert.True(filter.IsJunk(path, 4096));
    }

    [Fact]
    public void ShouldProcess_Is_Inverse_Of_IsJunk()
    {
        var filter = new JunkFilter();
        var junkPath = TempFile("junk.tmp", size: 4096);
        var goodPath = TempFile("report.pdf", size: 4096);

        Assert.False(filter.ShouldProcess(junkPath, 4096));
        Assert.True(filter.ShouldProcess(goodPath, 4096));
    }

    [Fact]
    public void Case_Insensitive_Glob_Matching()
    {
        var filter = new JunkFilter();
        var path = TempFile("REPORT.CRDOWNLOAD", size: 4096);

        Assert.True(filter.IsJunk(path, 4096));
    }

    [Fact]
    public void Glob_Question_Mark_Matches_Single_Char()
    {
        var filter = new JunkFilter(new JunkFilterRules
        {
            BlockGlobs = JunkFilter.DefaultBlockGlobs.Append("file?.txt").ToList(),
        });

        Assert.True(filter.IsJunk(TempFile("file1.txt", size: 4096), 4096));
        Assert.True(filter.IsJunk(TempFile("fileA.txt", size: 4096), 4096));
        Assert.False(filter.IsJunk(TempFile("file12.txt", size: 4096), 4096));
    }

    [Fact]
    public void Rules_Property_Exposes_Current_Rules()
    {
        var rules = new JunkFilterRules
        {
            MinSizeBytes = 5000,
            AllowGlobs = new[] { "*.exe" },
        };
        var filter = new JunkFilter(rules);

        Assert.Equal(5000, filter.Rules.MinSizeBytes);
        Assert.Contains("*.exe", filter.Rules.AllowGlobs);
    }

    [Fact]
    public void Empty_AllowGlobs_Allows_Normal_Files()
    {
        var filter = new JunkFilter();
        var path = TempFile("report.pdf", size: 4096);

        Assert.False(filter.IsJunk(path, 4096));
    }
}
