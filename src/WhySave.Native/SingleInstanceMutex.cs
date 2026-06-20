namespace WhySave.Native;

public sealed class SingleInstanceMutex : IDisposable
{
    public const string DefaultMutexName = @"Global\WhySaveSingleInstance";

    private readonly Mutex _mutex;
    private bool _disposed;

    public bool IsFirstInstance { get; }

    public SingleInstanceMutex(string mutexName = DefaultMutexName)
    {
        _mutex = new Mutex(initiallyOwned: true, name: mutexName, createdNew: out var createdNew);
        IsFirstInstance = createdNew;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            if (_mutex.SafeWaitHandle.IsClosed == false)
                _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        _mutex.Dispose();
    }
}
