using System.Collections.Concurrent;

namespace WhySave.Tests.Core;

public sealed class FakeTimeProvider : WhySave.Core.ITimeProvider
{
    private DateTimeOffset _now;
    private readonly ConcurrentDictionary<int, PendingDelay> _pending = new();
    private int _nextId;

    public FakeTimeProvider(DateTimeOffset? start = null)
    {
        _now = start ?? DateTimeOffset.UtcNow;
    }

    public DateTimeOffset UtcNow => _now;

    public Task Delay(TimeSpan delay, CancellationToken ct = default)
    {
        var due = _now + delay;
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource();

        var pending = new PendingDelay(id, due, tcs);
        _pending[id] = pending;

        ct.Register(() =>
        {
            _pending.TryRemove(id, out _);
            tcs.TrySetCanceled(ct);
        });

        if (due <= _now)
        {
            _pending.TryRemove(id, out _);
            tcs.TrySetResult();
        }

        return tcs.Task;
    }

    public void Advance(TimeSpan amount)
    {
        _now += amount;
        foreach (var pending in _pending.Values.Where(p => p.Due <= _now).ToList())
        {
            if (_pending.TryRemove(pending.Id, out _))
                pending.Tcs.TrySetResult();
        }
    }

    public void SetUtcNow(DateTimeOffset now)
    {
        _now = now;
        foreach (var pending in _pending.Values.Where(p => p.Due <= _now).ToList())
        {
            if (_pending.TryRemove(pending.Id, out _))
                pending.Tcs.TrySetResult();
        }
    }

    private sealed record PendingDelay(int Id, DateTimeOffset Due, TaskCompletionSource Tcs);
}
