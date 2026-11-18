using CordisSharp.Core;
using Xunit;

namespace CordisSharp.Core.Tests;

public sealed class FiberLifecycleTests
{
    private interface IDb
    {
        string Name { get; }
    }

    private interface ILog
    {
    }

    private sealed record Db(string Name) : IDb;

    private sealed class Logger : ILog
    {
    }

    private sealed class Log
    {
        public List<string> Lines { get; } = [];
    }

    private sealed class Pool : IDb
    {
        public bool Open { get; private set; } = true;

        public string Name => "pool";

        public void Close() => Open = false;
    }

    /// <summary>Creates a pool and provides it as IDb through its own activation scope.</summary>
    private sealed class PoolProvider(Log log) : IComponent
    {
        public IReadOnlySet<Type> Declared { get; } = new HashSet<Type>();

        public Pool Pool { get; private set; } = null!;

        public async IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> Effect(IComponentContext ctx)
        {
            yield return () =>
            {
                Pool = new Pool();
                log.Lines.Add("pool-created");
                return new ValueTask<Func<ValueTask>>(() =>
                {
                    Pool.Close();
                    log.Lines.Add("pool-closed");
                    return default;
                });
            };

            // The binding is a tracked change of this activation scope: it unwinds with us.
            await ctx.Provide<IDb>(Pool);
        }
    }

    /// <summary>Uses the pool; its teardown reads the dependency it is losing.</summary>
    private sealed class PoolUser(Log log) : IComponent
    {
        public IReadOnlySet<Type> Declared { get; } = new HashSet<Type> { typeof(IDb) };

        public int Activations { get; private set; }

        public bool ToreDownWithOpenPool { get; private set; }

        public async IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> Effect(IComponentContext ctx)
        {
            yield return () =>
            {
                Activations++;
                return new ValueTask<Func<ValueTask>>(() =>
                {
                    ToreDownWithOpenPool = ctx.Get<IDb>() is Pool { Open: true };
                    log.Lines.Add(ToreDownWithOpenPool ? "user-teardown-open" : "user-teardown-closed");
                    return default;
                });
            };
        }
    }

    /// <summary>Declares IDb and provides itself as ILog, so it is both consumer and provider.</summary>
    private sealed class Middle(Log log) : IComponent, ILog
    {
        public IReadOnlySet<Type> Declared { get; } = new HashSet<Type> { typeof(IDb) };

        public async IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> Effect(IComponentContext ctx)
        {
            yield return () => new ValueTask<Func<ValueTask>>(() =>
            {
                var open = ctx.Get<IDb>() is Pool { Open: true };
                log.Lines.Add(open ? "middle-teardown-open" : "middle-teardown-closed");
                return default;
            });

            await ctx.Provide<ILog>(this);
        }
    }

    [Fact]
    public async Task Withdrawing_a_provider_runs_its_dependents_first()
    {
        var log = new Log();
        var host = new CompositionHost();
        var provider = new PoolProvider(log);
        var user = new PoolUser(log);

        var added = await host.Add(provider);
        await host.Add(user);

        await added.DisposeAsync();

        // The dependent's teardown saw an open pool; the pool closed only after it was done.
        Assert.Equal(["pool-created", "user-teardown-open", "pool-closed"], log.Lines);
        Assert.True(user.ToreDownWithOpenPool);
        Assert.Equal(FiberState.Inactive, host.StateOf(user));
    }

    [Fact]
    public async Task Retire_stops_providing_but_keeps_the_fiber_registered()
    {
        var log = new Log();
        var host = new CompositionHost();
        var provider = new PoolProvider(log);
        var user = new PoolUser(log);

        await host.Add(provider);
        await host.Add(user);

        await host.Retire(provider);

        Assert.Equal(["pool-created", "user-teardown-open", "pool-closed"], log.Lines);
        Assert.Equal(FiberState.Inactive, host.StateOf(provider));
        Assert.Equal(FiberState.Inactive, host.StateOf(user));

        // The retired provider never comes back; the dependent revives on a fresh binding.
        await host.Provide<IDb>(new Pool());
        Assert.Equal(FiberState.Inactive, host.StateOf(provider));
        Assert.Equal(FiberState.Active, host.StateOf(user));
        Assert.Equal(2, user.Activations);
    }

    [Fact]
    public async Task An_activation_failure_is_recorded_without_failing_the_call()
    {
        var host = new CompositionHost();
        await host.Provide<IDb>(new Db("a"));

        var sneaky = new Sneaky();
        var healthy = new PoolUser(new Log());

        await host.Add(sneaky);   // completes instead of throwing

        Assert.Equal(FiberState.Failed, host.StateOf(sneaky));
        Assert.IsType<InvalidOperationException>(host.FailureOf(sneaky));

        await host.Add(healthy);   // a sibling is untouched
        Assert.Equal(FiberState.Active, host.StateOf(healthy));

        // A neutral change does not retry the failed fiber.
        await host.Provide<ILog>(new Logger());
        Assert.Equal(1, sneaky.Attempts);

        // A change behind a declared key is a new environment: one more attempt.
        await host.Provide<IDb>(new Db("b"));
        Assert.Equal(2, sneaky.Attempts);
        Assert.Equal(FiberState.Failed, host.StateOf(sneaky));
    }

    [Fact]
    public async Task States_are_observable()
    {
        var host = new CompositionHost();
        var user = new PoolUser(new Log());

        Assert.Equal(FiberState.Absent, host.StateOf(user));

        await host.Add(user);
        Assert.Equal(FiberState.Inactive, host.StateOf(user));   // parked, no error

        await host.Provide<IDb>(new Pool());
        Assert.Equal(FiberState.Active, host.StateOf(user));
    }

    [Fact]
    public async Task Teardown_cascades_depth_first_with_providers_last()
    {
        var log = new Log();
        var host = new CompositionHost();
        var provider = new PoolProvider(log);
        var middle = new Middle(log);
        var user = new LogUser(log);

        await host.Add(provider);
        await host.Add(middle);
        await host.Add(user);

        await host.Retire(provider);

        // Provider, then middle, then user: the deepest consumer tears down first, and
        // every provider is still alive when its dependents need it.
        Assert.Equal(
            ["pool-created", "user-teardown", "middle-teardown-open", "pool-closed"],
            log.Lines);
    }

    /// <summary>Declares only ILog; a bottom consumer in the chain.</summary>
    private sealed class LogUser(Log log) : IComponent
    {
        public IReadOnlySet<Type> Declared { get; } = new HashSet<Type> { typeof(ILog) };

        public async IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> Effect(IComponentContext ctx)
        {
            yield return () => new ValueTask<Func<ValueTask>>(() =>
            {
                _ = ctx.Get<ILog>();
                log.Lines.Add("user-teardown");
                return default;
            });
        }
    }

    /// <summary>Declares IDb but reaches for a key it never declared.</summary>
    private sealed class Sneaky : IComponent
    {
        public IReadOnlySet<Type> Declared { get; } = new HashSet<Type> { typeof(IDb) };

        public int Attempts { get; private set; }

        public async IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> Effect(IComponentContext ctx)
        {
            yield return () =>
            {
                Attempts++;
                _ = ctx.Get<ILog>();
                return new ValueTask<Func<ValueTask>>(() => default);
            };
        }
    }
}
