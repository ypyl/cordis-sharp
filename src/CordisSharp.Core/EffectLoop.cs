namespace CordisSharp.Core;

/// <summary>
/// The effect iterator: a component performs a sequence of changes, not one, and every
/// yield is a checkpoint where the partial accumulator can still be reversed.
/// </summary>
public static class EffectLoop
{
    /// <summary>
    /// Run the steps of a component effect against the scope. Each yielded step is a change
    /// that is tracked as it runs. Cancellation is checked between steps only, never mid-change
    /// (the inertial host of the paper). If the loop stops early, whether by cancellation or by
    /// a step that fails, what ran is recovered and the exception is rethrown: what ran is
    /// reversed, what did not run never happened.
    /// </summary>
    public static async ValueTask RunAsync(
        IEffectScope scope,
        IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> steps,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await foreach (var step in steps.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                await scope.Track(step).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch
        {
            // Failure unwinds partial state. If an inverse itself throws, that exception
            // replaces the original one: a broken inverse deserves to surface, not to hide
            // behind whatever interrupted the loop.
            await scope.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
