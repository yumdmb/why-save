using System.Text.Json.Serialization;
using System.Windows.Input;

namespace WhySave.App.Services;

public sealed record HotKeyDescriptor
{
    public static readonly HotKeyDescriptor Default = new()
    {
        Modifiers = HotKeyModifier.Control | HotKeyModifier.Win,
        Key = Key.Y,
    };

    public HotKeyModifier Modifiers { get; set; }

    public Key Key { get; set; }

    [JsonIgnore]
    public bool IsValid => Key != Key.None;

    public uint ToNativeModifiers()
    {
        uint native = 0;
        if (Modifiers.HasFlag(HotKeyModifier.Alt)) native |= WhySave.Native.HotKeyRegistrar.MOD_ALT;
        if (Modifiers.HasFlag(HotKeyModifier.Control)) native |= WhySave.Native.HotKeyRegistrar.MOD_CONTROL;
        if (Modifiers.HasFlag(HotKeyModifier.Shift)) native |= WhySave.Native.HotKeyRegistrar.MOD_SHIFT;
        if (Modifiers.HasFlag(HotKeyModifier.Win)) native |= WhySave.Native.HotKeyRegistrar.MOD_WIN;
        return native;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotKeyModifier.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotKeyModifier.Win)) parts.Add("Win");
        if (Modifiers.HasFlag(HotKeyModifier.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotKeyModifier.Shift)) parts.Add("Shift");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }
}

[Flags]
public enum HotKeyModifier
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

public sealed class JunkRulesSettings
{
    public List<string> BlockGlobs { get; set; } = new();
    public List<string> AllowGlobs { get; set; } = new();
    public long MinSizeBytes { get; set; } = 1024;
}

public sealed class AppSettings
{
    public List<string> WatchedFolders { get; set; } = new();

    public JunkRulesSettings JunkRules { get; set; } = new();

    public HotKeyDescriptor HotKey { get; set; } = HotKeyDescriptor.Default.Clone();

    public bool StartWithWindows { get; set; } = true;

    public string AutoUpdateChannel { get; set; } = "off";

    public bool LogLevelVerbose { get; set; } = false;
}

public static class HotKeyDescriptorExtensions
{
    public static HotKeyDescriptor Clone(this HotKeyDescriptor descriptor) =>
        new()
        {
            Modifiers = descriptor.Modifiers,
            Key = descriptor.Key,
        };
}
