using CordisSharp.Core;
using Xunit;

namespace CordisSharp.Core.Tests;

public sealed class EffectScopeTests
{
    /// <summary>A tiny shared environment: names get added, inverses remove them.</summary>
    private sealed class Ledger
    {
        public List<string> Log { get; } = [];

        public HashSet<string> Live { get; } = [];

        public Func<ValueTask<Func<ValueTask>>> Add(string name) => () =>
        {
            Log.Add($"do:{name}");
            Live.Add(name);
            return new ValueTask<Func<ValueTask>>(() =>
            {
                Log.Add($"undo:{name}");
                Live.Remove(name);
                return default;
            });
        };
    }

    [Fact]
    public async Task Disposing_out_of_order_reverses_each_change_exactly_once()
    {
        var scope = new EffectScope();
        var ledger = new Ledger();
        var a = await scope.Track(ledger.Add("a"));
        var b = await scope.Track(ledger.Add("b"));
        var c = await scope.Track(ledger.Add("c"));

        await b.DisposeAsync();

        Assert.DoesNotContain("b", ledger.Live);
        Assert.Equal(["do:a", "do:b", "do:c", "undo:b"], ledger.Log);

        await scope.RecoverAsync();

        Assert.Empty(ledger.Live);
        Assert.Equal(["do:a", "do:b", "do:c", "undo:b", "undo:c", "undo:a"], ledger.Log);
    }

    [Fact]
    public async Task Interrupting_halfway_reverses_only_what_ran()
    {
        var scope = new EffectScope();
        var ledger = new Ledger();
        var cts = new CancellationTokenSource();

        var steps = FourSteps(ledger, cts);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => EffectLoop.RunAsync(scope, steps, cts.Token).AsTask());

        Assert.Equal(["do:1", "do:2", "undo:2", "undo:1"], ledger.Log);
        Assert.Empty(ledger.Live);
    }

    [Fact]
    public async Task Double_dispose_is_a_noop()
    {
        var scope = new EffectScope();
        var ledger = new Ledger();
        var a = await scope.Track(ledger.Add("a"));

        a.Dispose();
        a.Dispose();

        Assert.Equal(["do:a", "undo:a"], ledger.Log);
        Assert.Empty(ledger.Live);
    }

    [Fact]
    public async Task Dispose_after_scope_recovery_is_a_noop()
    {
        var scope = new EffectScope();
        var ledger = new Ledger();
        var a = await scope.Track(ledger.Add("a"));

        await scope.RecoverAsync();
        a.Dispose();

        Assert.Equal(["do:a", "undo:a"], ledger.Log);
    }

    [Fact]
    public async Task Parent_recovery_recovers_a_disposed_child_exactly_once()
    {
        var parent = new EffectScope();
        var ledger = new Ledger();

        await parent.Track(ledger.Add("p"));
        var child = parent.CreateChild();
        await child.Track(ledger.Add("c"));

        await child.DisposeAsync();
        Assert.Equal(["do:p", "do:c", "undo:c"], ledger.Log);

        await parent.RecoverAsync();

        Assert.Empty(ledger.Live);
        Assert.Equal(["do:p", "do:c", "undo:c", "undo:p"], ledger.Log);
    }

    [Fact]
    public async Task Parent_recovery_retires_children_the_parent_never_disposed()
    {
        var parent = new EffectScope();
        var ledger = new Ledger();

        await parent.Track(ledger.Add("p"));
        var child = parent.CreateChild();
        await child.Track(ledger.Add("c"));
        var grandchild = child.CreateChild();
        await grandchild.Track(ledger.Add("g"));

        await parent.RecoverAsync();

        // One level at a time: the child's slot recovers the child, which recovers the grandchild.
        Assert.Empty(ledger.Live);
        Assert.Equal(["do:p", "do:c", "do:g", "undo:g", "undo:c", "undo:p"], ledger.Log);
    }

    [Fact]
    public async Task Tracking_after_recovery_throws()
    {
        var scope = new EffectScope();
        var ledger = new Ledger();
        await scope.RecoverAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => scope.Track(ledger.Add("a")).AsTask());
    }

    [Fact]
    public async Task Sync_dispose_rejects_async_inverses()
    {
        var scope = new EffectScope();
        var pending = new TaskCompletionSource();
        var handle = await scope.Track(() => new ValueTask<Func<ValueTask>>(
            () => new ValueTask(pending.Task)));

        Assert.Throws<InvalidOperationException>(handle.Dispose);
    }

    [Fact]
    public async Task A_failing_step_unwinds_what_ran()
    {
        var scope = new EffectScope();
        var ledger = new Ledger();
        var broken = new InvalidOperationException("step 2 broke");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => EffectLoop.RunAsync(scope, FailOnStepTwo(ledger, broken)).AsTask());

        // The failing change threw before it did anything, so only step 1 ran, and only
        // step 1 gets reversed. The original exception survives the unwind.
        Assert.Same(broken, thrown);
        Assert.Equal(["do:1", "undo:1"], ledger.Log);
        Assert.Empty(ledger.Live);
    }

    private static async IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> FourSteps(
        Ledger ledger, CancellationTokenSource cts)
    {
        yield return ledger.Add("1");
        // The second change cancels as a side effect, so the checkpoint after it
        // stops the loop before step 3 ever runs.
        yield return CancelAfter(ledger.Add("2"), cts);
        yield return ledger.Add("3");
        yield return ledger.Add("4");
    }

    private static Func<ValueTask<Func<ValueTask>>> CancelAfter(
        Func<ValueTask<Func<ValueTask>>> change, CancellationTokenSource cts) => async () =>
    {
        var inverse = await change();
        cts.Cancel();
        return inverse;
    };

    private static async IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> FailOnStepTwo(
        Ledger ledger, Exception exception)
    {
        yield return ledger.Add("1");
        yield return () => throw exception;
        yield return ledger.Add("3");
    }
}
