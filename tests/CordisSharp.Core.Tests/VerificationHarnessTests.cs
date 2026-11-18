using CordisSharp.Core;
using Xunit;

namespace CordisSharp.Core.Tests;

public sealed class VerificationHarnessTests
{
    private interface IDb
    {
    }

    private sealed class Db : IDb
    {
    }

    /// <summary>Tracks a dictionary add and forgets to undo it.</summary>
    private sealed class Sloppy(Dictionary<string, string> shared) : IComponent
    {
        public IReadOnlySet<Type> Declared { get; } = new HashSet<Type>();

        public async IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> Effect(IComponentContext ctx)
        {
            yield return () =>
            {
                shared["entry"] = "1";
                return new ValueTask<Func<ValueTask>>(() =>
                {
                    // The wrong inverse: forgets the entry entirely.
                    return default;
                });
            };
        }
    }

    /// <summary>Tracks a dictionary add and removes it again.</summary>
    private sealed class Neat(Dictionary<string, string> shared) : IComponent
    {
        public IReadOnlySet<Type> Declared { get; } = new HashSet<Type>();

        public async IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> Effect(IComponentContext ctx)
        {
            yield return () =>
            {
                shared["entry"] = "1";
                return new ValueTask<Func<ValueTask>>(() =>
                {
                    shared.Remove("entry");
                    return default;
                });
            };
        }
    }

    [Fact]
    public async Task A_wrong_inverse_is_caught()
    {
        var host = new CompositionHost();
        var shared = new Dictionary<string, string>();
        var harness = new VerificationHarness(host);
        harness.Watch("shared", () => shared.Count);

        var before = harness.Capture();

        var added = await host.Add(new Sloppy(shared));
        await added.DisposeAsync();

        var leak = Assert.Throws<CompositionLeakException>(() => harness.Verify(before));
        var line = Assert.Single(leak.Differences);
        Assert.Contains("shared", line);
        Assert.Contains("expected 0", line);
        Assert.Contains("found 1", line);
    }

    [Fact]
    public async Task A_clean_cycle_passes_verification()
    {
        var host = new CompositionHost();
        var shared = new Dictionary<string, string>();
        var harness = new VerificationHarness(host);
        harness.Watch("shared", () => shared.Count);

        var before = harness.Capture();

        var added = await host.Add(new Neat(shared));
        await added.DisposeAsync();

        harness.Verify(before);   // no throw, nothing leaked
        Assert.Empty(shared);
    }

    [Fact]
    public async Task An_undisposed_provide_is_reported_by_the_registry_watch()
    {
        var host = new CompositionHost();
        var harness = new VerificationHarness(host);

        var before = harness.Capture();

        await host.Provide<IDb>(new Db());   // never undone

        var leak = Assert.Throws<CompositionLeakException>(() => harness.Verify(before));
        Assert.Contains("registry: IDb still bound", leak.Message);
    }
}
