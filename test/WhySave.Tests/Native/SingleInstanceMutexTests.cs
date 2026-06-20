using WhySave.Native;

namespace WhySave.Tests.Native;

public class SingleInstanceMutexTests
{
    [Fact]
    public void First_Handle_Claims_First_Instance()
    {
        using var mutex = new SingleInstanceMutex(@"Global\WhySaveTestMutex_" + Guid.NewGuid().ToString("N"));
        Assert.True(mutex.IsFirstInstance);
    }

    [Fact]
    public void Second_Handle_With_Same_Name_Is_Not_First_Instance()
    {
        var name = @"Global\WhySaveTestMutex_" + Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceMutex(name);
        using var second = new SingleInstanceMutex(name);

        Assert.True(first.IsFirstInstance);
        Assert.False(second.IsFirstInstance);
    }

    [Fact]
    public void After_First_Disposes_Next_Handle_Becomes_First_Instance()
    {
        var name = @"Global\WhySaveTestMutex_" + Guid.NewGuid().ToString("N");
        var first = new SingleInstanceMutex(name);
        Assert.True(first.IsFirstInstance);
        first.Dispose();

        Thread.Sleep(50);

        using var second = new SingleInstanceMutex(name);
        Assert.True(second.IsFirstInstance);
    }
}

public class HotKeyRegistrarTests
{
    [Fact]
    public void UnregisterHotKey_For_Unregistered_Key_Returns_False()
    {
        var result = HotKeyRegistrar.UnregisterHotKey(IntPtr.Zero, 0);
        Assert.False(result);
    }
}
