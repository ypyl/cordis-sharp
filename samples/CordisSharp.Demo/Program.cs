using CordisSharp.Core;

namespace CordisSharp.Demo;

/// <summary>
/// A walk through the whole runtime in one console session: parking, activation on
/// arrival, two-phase teardown, revival on a fresh provider, instantiation as an
/// effect, recorded failure, and the verification harness at the end.
/// </summary>
internal sealed class Program
{
    private static async Task Main()
    {
        var host = new CompositionHost();
        var harness = new VerificationHarness(host);

        // The piece of environment the harness can check: nothing may leak a cache.
        harness.Watch("cache instances alive", () => MemoryCache.AliveInstances);

        var before = harness.Capture();

        Section("1. A component with no provider parks instead of failing");
        var worker = new Worker("worker-1");
        var workerHandle = await host.Add(worker);
        Console.WriteLine($"    worker-1 state: {host.StateOf(worker)} (parked, no exception)");

        Section("2. The dependency chain arrives and everything activates in order");
        var configHandle = await host.Provide<IAppConfig>(new AppConfig("key-1"));
        var cacheService = new CacheService();
        var cacheHandle = await host.Add(cacheService);
        Console.WriteLine($"    worker-1 state: {host.StateOf(worker)}, runs: {worker.Runs}");

        Section("3. Retire the cache provider: dependents tear down first");
        await host.Retire(cacheService);
        Console.WriteLine($"    worker-1 state: {host.StateOf(worker)} (parked again)");

        Section("4. A fresh provider revives the dependent");
        var cacheService2 = new CacheService();
        var cacheHandle2 = await host.Add(cacheService2);
        Console.WriteLine($"    worker-1 runs: {worker.Runs} (reactivated)");

        Section("5. A parent instantiates children; unloading the parent retires them");
        var team = new Team("team-a");
        var teamHandle = await host.Add(team);
        await teamHandle.DisposeAsync();

        Section("6. A failing component records the error; the host keeps running");
        var faulty = new Faulty();
        await host.Add(faulty);
        Console.WriteLine($"    faulty state: {host.StateOf(faulty)}");
        Console.WriteLine($"    recorded error: {host.FailureOf(faulty)!.Message}");

        Section("7. Unload everything; the harness confirms nothing leaked");
        await host.DisposeAsync();
        harness.Verify(before);
        Console.WriteLine("    verification passed: every cache instance disposed, registry empty");
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"== {title}");
    }
}

internal interface IAppConfig
{
    string ApiKey { get; }
}

internal interface ICache
{
}

internal sealed record AppConfig(string ApiKey) : IAppConfig;

/// <summary>A resource with a visible lifetime, so the teardown order is observable.</summary>
internal sealed class MemoryCache : ICache, IDisposable
{
    internal static int AliveInstances;

    private static int _sequence;

    public string Name { get; }

    public bool Disposed { get; private set; }

    public MemoryCache(string name)
    {
        Name = name;
        AliveInstances++;
    }

    public void Dispose()
    {
        Disposed = true;
        AliveInstances--;
    }

    public static string NextName(string apiKey) => $"cache-{++_sequence}-for-{apiKey}";
}

/// <summary>A provider that owns a resource: creates a cache and provides it.</summary>
internal sealed class CacheService : IComponent
{
    public IReadOnlySet<Type> Declared { get; } = new HashSet<Type> { typeof(IAppConfig) };

    public async IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> Effect(IComponentContext ctx)
    {
        MemoryCache? cache = null;
        yield return () =>
        {
            cache = new MemoryCache(MemoryCache.NextName(ctx.Get<IAppConfig>().ApiKey));
            Console.WriteLine($"    [cache] {cache.Name} created");
            return new ValueTask<Func<ValueTask>>(() =>
            {
                cache.Dispose();
                Console.WriteLine($"    [cache] {cache.Name} disposed");
                return default;
            });
        };

        await ctx.Provide<ICache>(cache!);
    }
}

/// <summary>A consumer that reads the dependency it is losing during its own teardown.</summary>
internal sealed class Worker(string name) : IComponent
{
    public IReadOnlySet<Type> Declared { get; } = new HashSet<Type> { typeof(ICache) };

    public int Runs { get; private set; }

    public async IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> Effect(IComponentContext ctx)
    {
        yield return () =>
        {
            Runs++;
            var cache = (MemoryCache)ctx.Get<ICache>();
            Console.WriteLine($"    [{name}] started, using {cache.Name}");
            return new ValueTask<Func<ValueTask>>(() =>
            {
                var live = (MemoryCache)ctx.Get<ICache>();
                Console.WriteLine($"    [{name}] stopping; the cache it is losing is still usable: {!live.Disposed}");
                return default;
            });
        };
    }
}

/// <summary>A parent whose children exist as its own recorded effects.</summary>
internal sealed class Team(string name) : IComponent
{
    public IReadOnlySet<Type> Declared { get; } = new HashSet<Type> { typeof(ICache) };

    public async IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> Effect(IComponentContext ctx)
    {
        yield return () => new ValueTask<Func<ValueTask>>(() =>
        {
            Console.WriteLine($"    [{name}] retired");
            return default;
        });

        // Instantiation as an effect: the children are inverses of this parent.
        await ctx.Add(new Worker($"{name}/w1"));
        await ctx.Add(new Worker($"{name}/w2"));
    }
}

/// <summary>A component with a bug: its activation throws.</summary>
internal sealed class Faulty : IComponent
{
    public IReadOnlySet<Type> Declared { get; } = new HashSet<Type> { typeof(ICache) };

    public async IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> Effect(IComponentContext ctx)
    {
        yield return () => throw new InvalidOperationException("boom: plugin bug");
    }
}
