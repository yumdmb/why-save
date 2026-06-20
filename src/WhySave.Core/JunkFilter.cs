using System.IO;
using System.Text.RegularExpressions;

namespace WhySave.Core;

public sealed class JunkFilterRules
{
    public IReadOnlyList<string> BlockGlobs { get; init; } = JunkFilter.DefaultBlockGlobs;
    public IReadOnlyList<string> AllowGlobs { get; init; } = Array.Empty<string>();
    public long MinSizeBytes { get; init; } = JunkFilter.DefaultMinSizeBytes;
}

public sealed class JunkFilter
{
    public const long DefaultMinSizeBytes = 1024;

    public static readonly IReadOnlyList<string> DefaultBlockGlobs = new[]
    {
        "*.crdownload",
        "*.tmp",
        "*.part",
        "*.download",
        "*.partial",
        "~$*",
        "*.bak",
        "thumbs.db",
        "desktop.ini",
        "*.lnk",
        "*.log",
    };

    public static readonly IReadOnlyList<string> DefaultUpdaterPatterns = new[]
    {
        "*chrome*setup*",
        "*chrome*installer*",
        "*update*.tmp",
        "*updater*.tmp",
        "*setup*.tmp",
        "*installer*.tmp",
        "*_setup.exe",
        "*crashpad*.exe",
    };

    private Func<string, long, bool> _predicate;
    private JunkFilterRules _rules;

    public JunkFilter() : this(new JunkFilterRules()) { }

    public JunkFilter(JunkFilterRules rules)
    {
        _rules = rules;
        _predicate = Compile(rules);
    }

    public JunkFilterRules Rules => _rules;

    public bool IsJunk(string path, long sizeBytes) => _predicate(path, sizeBytes);

    public bool ShouldProcess(string path, long sizeBytes) => !IsJunk(path, sizeBytes);

    public void UpdateRules(JunkFilterRules rules)
    {
        _rules = rules;
        _predicate = Compile(rules);
    }

    private static Func<string, long, bool> Compile(JunkFilterRules rules)
    {
        var blockMatchers = rules.BlockGlobs
            .Concat(DefaultUpdaterPatterns)
            .Select(GlobToRegex)
            .ToList();
        var allowMatchers = rules.AllowGlobs
            .Select(GlobToRegex)
            .ToList();
        var minSize = rules.MinSizeBytes;

        return (path, size) =>
        {
            if (size < minSize)
                return true;

            var filename = Path.GetFileName(path);
            var filenameLower = filename.ToLowerInvariant();

            foreach (var allow in allowMatchers)
            {
                if (allow.IsMatch(filenameLower))
                    return false;
            }

            foreach (var block in blockMatchers)
            {
                if (block.IsMatch(filenameLower))
                    return true;
            }

            return false;
        };
    }

    private static Regex GlobToRegex(string glob)
    {
        var pattern = "^" + Regex.Escape(glob.ToLowerInvariant())
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }
}
