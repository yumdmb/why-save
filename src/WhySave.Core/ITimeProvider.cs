namespace WhySave.Core;

public interface ITimeProvider
{
    DateTimeOffset UtcNow { get; }
    Task Delay(TimeSpan delay, CancellationToken ct = default);
}

public sealed class SystemTimeProvider : ITimeProvider
{
    public static readonly SystemTimeProvider Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task Delay(TimeSpan delay, CancellationToken ct = default) =>
        Task.Delay(delay, ct);
}
