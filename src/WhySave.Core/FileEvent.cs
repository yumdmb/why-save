using System.Threading.Channels;

namespace WhySave.Core;

public enum FileEventKind
{
    Created,
    Renamed,
    Deleted,
}

public record FileEvent(string Path, FileEventKind Kind, DateTimeOffset At);

public interface IFileEventSink
{
    void Post(FileEvent evt);
}

public sealed class ChannelFileEventSink : IFileEventSink
{
    private readonly Channel<FileEvent> _channel;

    public Channel<FileEvent> Channel => _channel;

    public ChannelFileEventSink(Channel<FileEvent> channel)
    {
        _channel = channel;
    }

    public void Post(FileEvent evt) => _channel.Writer.TryWrite(evt);
}
