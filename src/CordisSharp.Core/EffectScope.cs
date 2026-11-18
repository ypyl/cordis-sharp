namespace CordisSharp.Core;

/// <summary>
/// One scope, one lever: runs changes, folds inverses, and recovers them last-in-first-out.
/// A scope is single-use: once recovered it refuses further tracking.
/// Not thread-safe; a scope and its handles belong to one logical flow.
/// </summary>
public sealed class EffectScope : IEffectScope
{
    // Insertion order; recovery walks backwards. The node handle in TrackedChange
    // gives O(1) removal from the middle for out-of-order disposal.
    private readonly LinkedList<Func<ValueTask>> _undo = new();
    private bool _recovered;

    /// <summary>Create a root scope with no parent.</summary>
    public EffectScope()
    {
    }

    /// <summary>
    /// A child scope whose entire recovery is one inverse of this scope, ordered at creation.
    /// Recovering the parent later retires the child even if nobody disposed it. If the child
    /// has already recovered, the folded inverse is a no-op. This is the seed of "instantiation
    /// is an effect" (M4): the child's existence is a recorded change the parent can reverse.
    /// </summary>
    public EffectScope CreateChild()
    {
        ThrowIfRecovered();
        var child = new EffectScope();
        _undo.AddLast(() => child.RecoverAsync());
        return child;
    }

    public async ValueTask<TrackedChange> Track(Func<ValueTask<Func<ValueTask>>> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        ThrowIfRecovered();

        var inverse = await change().ConfigureAwait(false);
        var node = _undo.AddLast(inverse ?? throw new InvalidOperationException(
            "A tracked change must return an inverse; returning null would break recovery."));
        return new TrackedChange(node);
    }

    /// <summary>
    /// <c>recover</c>: run the accumulated inverses sequentially, last-in-first-out, at most once.
    /// </summary>
    public async ValueTask RecoverAsync()
    {
        if (_recovered)
        {
            return;
        }

        _recovered = true;
        while (_undo.Last is { } last)
        {
            // Detach before running so the inverse cannot run twice, whatever happens to it.
            _undo.RemoveLast();
            await last.Value().ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync() => RecoverAsync();

    internal bool Recovered => _recovered;

    internal void ThrowIfRecovered()
    {
        if (_recovered)
        {
            throw new ObjectDisposedException(
                nameof(EffectScope), "The scope was recovered; tracking into it again is not allowed.");
        }
    }
}
