namespace CordisSharp.Core;

/// <summary>
/// The handle to one tracked change. Disposable for the ordinary case, awaitable for the rest.
/// Dispose runs the inverse immediately, out of order; the scope still recovers everything
/// that is left when its time comes.
/// </summary>
public sealed class TrackedChange : IDisposable, IAsyncDisposable
{
    private readonly LinkedListNode<Func<ValueTask>> _node;
    private bool _undone;

    internal TrackedChange(LinkedListNode<Func<ValueTask>> node) => _node = node;

    /// <summary>
    /// No-op if the change was already undone here, or the scope already recovered it.
    /// One reversal per change, always.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!Take())
        {
            return;
        }

        await _node.Value().ConfigureAwait(false);
    }

    /// <summary>
    /// The synchronous path. It works only when the inverse completes synchronously;
    /// if the inverse is genuinely asynchronous, this throws rather than discarding the
    /// remaining work, and the caller must await <see cref="DisposeAsync"/> instead.
    /// </summary>
    public void Dispose()
    {
        if (!Take())
        {
            return;
        }

        var undo = _node.Value();
        if (!undo.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException(
                "The inverse of this change is asynchronous. Await DisposeAsync instead of calling Dispose.");
        }

        undo.GetAwaiter().GetResult();
    }

    private bool Take()
    {
        if (_undone)
        {
            return false;
        }

        // A detached node means the scope already recovered and ran this inverse.
        if (_node.List is null)
        {
            _undone = true;
            return false;
        }

        _node.List.Remove(_node);
        _undone = true;
        return true;
    }
}
