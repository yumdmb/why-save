using System.Windows.Input;
using WhySave.App.Services;
using WhySave.Native;

namespace WhySave.Tests.App;

public class HotKeyParserTests
{
    [Theory]
    [InlineData("Ctrl+Win+Y", HotKeyModifier.Control | HotKeyModifier.Win, Key.Y)]
    [InlineData("Ctrl+Y", HotKeyModifier.Control, Key.Y)]
    [InlineData("Alt+Shift+P", HotKeyModifier.Alt | HotKeyModifier.Shift, Key.P)]
    [InlineData("Win+1", HotKeyModifier.Win, Key.D1)]
    public void TryParse_Valid_Inputs_Returns_Expected_Descriptor(
        string input, HotKeyModifier expectedModifiers, Key expectedKey)
    {
        var parsed = HotKeyParser.TryParse(input, out var descriptor);

        Assert.True(parsed);
        Assert.Equal(expectedModifiers, descriptor.Modifiers);
        Assert.Equal(expectedKey, descriptor.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Win")]
    [InlineData("FancyKey")]
    public void TryParse_Invalid_Inputs_Returns_False(string input)
    {
        var parsed = HotKeyParser.TryParse(input, out _);
        Assert.False(parsed);
    }

    [Fact]
    public void ToNativeModifiers_Default_Ctrl_Win_Y_Maps_To_Control_And_Win()
    {
        var descriptor = HotKeyDescriptor.Default;

        var native = descriptor.ToNativeModifiers();

        Assert.Equal(HotKeyRegistrar.MOD_CONTROL | HotKeyRegistrar.MOD_WIN, native);
    }

    [Fact]
    public void ToString_Renders_Modifiers_And_Key()
    {
        var descriptor = new HotKeyDescriptor
        {
            Modifiers = HotKeyModifier.Control | HotKeyModifier.Win,
            Key = Key.Y,
        };

        Assert.Equal("Ctrl+Win+Y", descriptor.ToString());
    }
}
