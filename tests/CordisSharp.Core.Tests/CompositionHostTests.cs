using CordisSharp.Core;
using Xunit;

namespace CordisSharp.Core.Tests;

public sealed class CompositionHostTests
{
    private interface IDb
    {
        string Name { get; }
    }

    private interface ILogger
    {
    }

    private sealed record Db(string Name) : IDb;

    /// <summary>Declares the given keys; each activation records the provider it saw.</summary>
    private sealed class Dependent : IComponent
    {
        public Dependent(params Type[] declared) => Declared = new HashSet<Type>(declared);

        public IReadOnlySet<Type> Declared { get; }

        public int Activations { get; private set; }

        public int Deactivations { get; private set; }

        public List<IDb> Seen { get; } = [];

        public async IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> Effect(IComponentContext ctx)
        {
            yield return () =>
            {
                Activations++;
                Seen.Add(ctx.Get<IDb>());
                return new ValueTask<Func<ValueTask>>(() =>
                {
                    Deactivations++;
                    return default;
                });
            };
        }
    }

    [Fact]
    public async Task A_missing_dependency_parks_silently_until_the_provider_arrives()
    {
        var host = new CompositionHost();
        var c = new Dependent(typeof(IDb));

        await host.Add(c);
        Assert.Equal(0, c.Activations);

        await host.Provide<IDb>(new Db("a"));
        Assert.Equal(1, c.Activations);
        Assert.Equal("a", c.Seen.Single().Name);
    }

    [Fact]
    public async Task A_swap_reactivates_on_provider_identity_even_when_the_value_is_equal()
    {
        var host = new CompositionHost();
        var c = new Dependent(typeof(IDb));

        await host.Add(c);
        await host.Provide<IDb>(new Db("x"));
        Assert.Equal(1, c.Activations);

        // A different instance, but Equals-equal: records compare by value.
        await host.Provide<IDb>(new Db("x"));

        Assert.Equal(2, c.Activations);
        Assert.Equal(1, c.Deactivations);
        Assert.Equal(c.Seen[0], c.Seen[1]);      // equal values
        Assert.NotSame(c.Seen[0], c.Seen[1]);    // different providers
    }

    [Fact]
    public async Task Undoing_a_swap_reactivates_against_the_restored_provider()
    {
        var host = new CompositionHost();
        var c = new Dependent(typeof(IDb));

        await host.Add(c);
        await host.Provide<IDb>(new Db("x"));
        var swap = await host.Provide<IDb>(new Db("x"));
        Assert.Equal(2, c.Activations);

        await swap.DisposeAsync();

        Assert.Equal(3, c.Activations);
        Assert.Equal(2, c.Deactivations);
        Assert.Same(c.Seen[0], c.Seen[2]);      // the first provider, back again
    }

    [Fact]
    public async Task Removing_the_provider_deactivates_and_parks_again()
    {
        var host = new CompositionHost();
        var c = new Dependent(typeof(IDb));
        await host.Add(c);
        var a = await host.Provide<IDb>(new Db("a"));
        Assert.Equal(1, c.Activations);

        await a.DisposeAsync();
        Assert.Equal(1, c.Deactivations);
        Assert.Equal(1, c.Activations);

        // Parked, not gone: a new provider revives it.
        await host.Provide<IDb>(new Db("b"));
        Assert.Equal(2, c.Activations);
        Assert.Equal("b", c.Seen[1].Name);
    }

    [Fact]
    public async Task Two_keys_activate_only_when_both_are_bound()
    {
        var host = new CompositionHost();
        var c = new Dependent(typeof(IDb), typeof(ILogger));
        await host.Add(c);

        await host.Provide<IDb>(new Db("a"));
        Assert.Equal(0, c.Activations);

        await host.Provide<ILogger>(new Logger());
        Assert.Equal(1, c.Activations);
    }

    [Fact]
    public async Task An_undeclared_access_fails_the_fiber_not_the_call()
    {
        var host = new CompositionHost();
        await host.Provide<IDb>(new Db("a"));

        var sneaky = new Sneaky();
        await host.Add(sneaky);

        // The call completes; the bug is recorded on the fiber.
        Assert.Equal(FiberState.Failed, host.StateOf(sneaky));
        Assert.IsType<InvalidOperationException>(host.FailureOf(sneaky));
    }

    [Fact]
    public async Task Teardown_can_still_read_the_key_it_is_losing()
    {
        var host = new CompositionHost();
        var tearing = new Tearing();

        await host.Provide<IDb>(new Db("a"));
        await host.Add(tearing);

        // Host recovery is LIFO: the component goes before the binding it depends on,
        // and its teardown reads the committed view while it is still there.
        await host.DisposeAsync();

        Assert.Equal("a", tearing.ToreDownWith);
    }

    [Fact]
    public async Task Undoing_an_add_stops_the_component_reacting()
    {
        var host = new CompositionHost();
        var c = new Dependent(typeof(IDb));
        await host.Provide<IDb>(new Db("a"));
        var added = await host.Add(c);
        Assert.Equal(1, c.Activations);

        await added.DisposeAsync();
        Assert.Equal(1, c.Deactivations);

        // Removed, not parked: later providers do not revive it.
        await host.Provide<IDb>(new Db("b"));
        Assert.Equal(1, c.Activations);
    }

    private sealed class Logger : ILogger
    {
    }

    /// <summary>Declares IDb but reaches for a key it never declared.</summary>
    private sealed class Sneaky : IComponent
    {
        public IReadOnlySet<Type> Declared { get; } = new HashSet<Type> { typeof(IDb) };

        public async IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> Effect(IComponentContext ctx)
        {
            yield return () =>
            {
                _ = ctx.Get<ILogger>();
                return new ValueTask<Func<ValueTask>>(() => default);
            };
        }
    }

    /// <summary>A component whose teardown reads the dependency it is losing.</summary>
    private sealed class Tearing : IComponent
    {
        public IReadOnlySet<Type> Declared { get; } = new HashSet<Type> { typeof(IDb) };

        public string? ToreDownWith { get; private set; }

        public async IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> Effect(IComponentContext ctx)
        {
            yield return () => new ValueTask<Func<ValueTask>>(() =>
            {
                ToreDownWith = ctx.Get<IDb>().Name;
                return default;
            });
        }
    }
}
