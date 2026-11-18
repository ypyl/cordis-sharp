namespace CordisSharp.Core;

/// <summary>
/// The effect context of the paper: the one place where changes to shared state are made,
/// holding an accumulator of inverses that grows as changes run.
/// </summary>
public interface IEffectScope : IAsyncDisposable
{
    /// <summary>
    /// <c>track</c>: run the change and fold the inverse it returns into the accumulator.
    /// The change must return the inverse that fits the state it just produced; that is the
    /// whole obligation, and the runtime does not check it.
    /// </summary>
    ValueTask<TrackedChange> Track(Func<ValueTask<Func<ValueTask>>> change);
}
