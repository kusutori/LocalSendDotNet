using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace LocalSendDotNet;

internal sealed class BroadcastHub<T>(int capacity)
{
    private readonly ConcurrentDictionary<long, Channel<T>> _subscribers = new();
    private long _nextId;
    private bool _completed;

    public void Publish(T item)
    {
        foreach (var channel in _subscribers.Values)
            channel.Writer.TryWrite(item);
    }

    public async Task PublishAsync(T item, CancellationToken cancellationToken)
    {
        foreach (var channel in _subscribers.Values)
            await channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<T> Subscribe([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_completed)
            yield break;

        var id = Interlocked.Increment(ref _nextId);
        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _subscribers[id] = channel;
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
        }
    }

    public void Complete()
    {
        _completed = true;
        foreach (var channel in _subscribers.Values)
            channel.Writer.TryComplete();
        _subscribers.Clear();
    }
}
