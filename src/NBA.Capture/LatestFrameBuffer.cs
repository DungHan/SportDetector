namespace NBA.Capture;

/// <summary>
/// Single-slot "latest value wins" buffer. A producer calling <see cref="Publish"/> faster than a consumer
/// drains it never grows a backlog - the buffer only ever holds the single newest value, overwriting whatever
/// was there before. Implements the frame-capture spec's "Frame delivery keeps pace with latest frame"
/// requirement; used internally by every <see cref="IFrameSource"/> implementation instead of a queue/channel.
/// </summary>
public sealed class LatestFrameBuffer<T>
    where T : class
{
    private readonly object _gate = new();
    private T? _latest;
    private TaskCompletionSource<T>? _waiter;

    /// <summary>Publishes a new value, overwriting any previously published value that was not yet consumed.</summary>
    public void Publish(T value)
    {
        TaskCompletionSource<T>? waiterToSignal;
        lock (_gate)
        {
            _latest = value;
            waiterToSignal = _waiter;
            _waiter = null;
        }

        // Signal outside the lock so a continuation running synchronously can't re-enter and deadlock.
        waiterToSignal?.TrySetResult(value);
    }

    /// <summary>Returns the latest published value without consuming it, or null if none has been published (or the buffer was cleared).</summary>
    public T? TryGetLatest()
    {
        lock (_gate)
        {
            return _latest;
        }
    }

    /// <summary>Drops the currently held value, if any (e.g. when switching capture sources).</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _latest = null;
        }
    }

    /// <summary>Completes the next time <see cref="Publish"/> is called after this method runs (not with an already-held value).</summary>
    public Task<T> WaitForNextAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _waiter ??= new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tcs = _waiter;
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            }

            return tcs.Task;
        }
    }
}
