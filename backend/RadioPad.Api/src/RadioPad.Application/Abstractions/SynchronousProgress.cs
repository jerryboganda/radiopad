namespace RadioPad.Application.Abstractions;

/// <summary>
/// An <see cref="IProgress{T}"/> that invokes its callback SYNCHRONOUSLY, inline on the
/// reporting thread, serialized under a lock.
///
/// <para><b>Why not <see cref="System.Progress{T}"/>:</b> the BCL implementation captures the
/// creating thread's <see cref="System.Threading.SynchronizationContext"/> (or, absent one, posts
/// to the <see cref="System.Threading.ThreadPool"/>) and delivers each report on that context. For
/// a token stream reported from a tight provider read loop that means chunks can be delivered late
/// and, worse, OUT OF ORDER — which would scramble the reconstructed partial text. AI-013 requires
/// strict arrival order, so every streaming AI producer uses this type, never
/// <see cref="System.Progress{T}"/>.</para>
/// </summary>
public sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    private readonly object _gate = new();

    public SynchronousProgress(Action<T> handler) =>
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    public void Report(T value)
    {
        lock (_gate)
            _handler(value);
    }
}
