# Guide: learning the core library through this demo

This guide follows `Program.cs` section by section. Run the demo first, read this with the output
next to you, and you will have used every public type the library has.

```
dotnet run --project samples/CordisSharp.Demo
```

## The model in five sentences

1. Every change to shared state goes through **track**: you run a change and hand back the
   inverse that fits what you just did. The runtime never checks your inverse; it folds it away
   and runs it when recovery comes.
2. A component **declares** the keys it needs. A declared key with no binding parks the
   component; it activates when the key arrives, and it reactivates when the provider behind
   the key changes, even if the new value equals the old one.
3. A **fiber** is one running component instance with a lifecycle. Only two things move it:
   somebody retires it, or the provider behind one of its declared keys changes.
4. Teardown is two-phase: everyone relying on what you provide goes first, while your
   resources are still alive; only then does your own accumulator run, undoing your changes
   last-in-first-out.
5. Nothing is tracked implicitly. State you change outside track, or fail to watch, is
   invisible to the runtime and to the verification harness.

## Section by section

### 1. A component with no provider parks instead of failing

```csharp
var workerHandle = await host.Add(worker);
Console.WriteLine($"    worker-1 state: {host.StateOf(worker)}");
```

`worker` declares `ICache` (see `Worker.Declared`). Nothing provides `ICache` yet, so `Add`
returns normally and the fiber sits at `Inactive`. No exception, no retry loop, no null to
guard against: the gap between adding a component and providing its dependency is a legal
state of the system.

### 2. The dependency chain arrives and everything activates in order

```csharp
var configHandle = await host.Provide<IAppConfig>(new AppConfig("key-1"));
var cacheHandle = await host.Add(cacheService);
```

Three things happen in this section, and the console shows them in order:

- `Provide<IAppConfig>` installs a binding with a fresh provider uid.
- Adding `CacheService`, which declares `IAppConfig`, activates immediately against it.
- `CacheService`'s effect calls `ctx.Provide<ICache>(cache)`. That installation notifies
  every fiber declaring `ICache`, and the parked `worker-1` activates against the new binding.

Two details worth noticing in `Worker.Effect`: the activation reads the binding through
`ctx.Get<ICache>()`, and `Get` reads the **committed view**, the snapshot taken at
activation. If the cache is swapped later, this worker keeps using the old one until it is
deactivated and reactivated. That is the paper's committed-view rule, and it is why teardown
never sees a binding it did not commit to.

Also notice what `Provide` and `Add` return: a `TrackedChange`. Providing and adding are
themselves effects, with inverses. Disposing that handle is how you withdraw a binding or
remove a component, and undoing the whole host is running all those inverses.

### 3. Retire the cache provider: dependents tear down first

```csharp
await host.Retire(cacheService);
```

Watch the output order:

```
    [worker-1] stopping; the cache it is losing is still usable: True
    [cache] cache-1-for-key-1 disposed
```

The worker's teardown ran while the cache was still open, and only then was the cache
disposed. That is two-phase teardown: phase one takes down every fiber committed to a binding
this provider owns, phase two runs the provider's accumulator. If you have ever written
teardown code that raced a connection pool being closed under it, this is the rule that
makes that class of bug impossible.

`Retire` is deliberately not removal. The retired fiber stays in the registry and never
reactivates, but only undoing its `Add`, or disposing the host, removes it. The worker, out
of things to read, parks again.

### 4. A fresh provider revives the dependent

```csharp
var cacheHandle2 = await host.Add(cacheService2);
Console.WriteLine($"    worker-1 runs: {worker.Runs}");
```

`worker.Runs` is 2. Nothing called the worker; the registry changed, the classifier noticed
that the `ICache` binding's provider uid moved, and the parked fiber activated against the
new snapshot. Reactivity is a corollary of the effect discipline: every binding change is a
tracked change, so the runtime knows exactly when "is my key satisfied" could have changed.

The unit tests push this further than the demo does: a swap with a value that is *equal* to
the old one still reactivates the dependent, because the classifier compares provider
identity (a uid that is never reused), not values.

### 5. A parent instantiates children; unloading the parent retires them

In `Team.Effect`:

```csharp
await ctx.Add(new Worker($"{name}/w1"));
await ctx.Add(new Worker($"{name}/w2"));
```

The children are added through the *parent's* context, so their existence is recorded as
tracked changes of the parent's own activation scope. Disposing the team's handle runs the
parent's accumulator, which retires the children, whose own accumulators run first. The
output is leaves-first (`w2`, then `w1`, then `team-a retired`) and there is no unload code
anywhere in `Team`. If you have ever written a component manager that remembers to tear down
children in the right order, this is that manager, deleted.

### 6. A failing component records the error; the host keeps running

```csharp
var faulty = new Faulty();
await host.Add(faulty);          // completes
host.FailureOf(faulty)!.Message   // "boom: plugin bug"
```

`Faulty.Effect` throws. `Add` completes anyway. The fiber lands at `Failed` with the exception
recorded on it, the host and every sibling keep running, and the failed fiber does not retry
until the registry actually changes, because the classifier only runs on registry changes.
One bug in one plugin used to mean one dead process; here it means one `Failed` fiber you can
query.

### 7. Unload everything; the harness confirms nothing leaked

```csharp
await host.DisposeAsync();
harness.Verify(before);
```

`DisposeAsync` recovers the root scope: every provide and every add, undone
last-in-first-out, which is why dependents always go before their providers. Then the
verification harness, the check the TypeScript reference never had:

- `harness.Watch("cache instances alive", () => MemoryCache.AliveInstances)` named one piece
  of the environment before anything ran. A probe is a function that reads the state you
  care about; the harness refuses to guess what you should care about.
- `Capture()` held the probe values plus the whole registry view.
- `Verify(before)` re-read everything and would have thrown one
  `CompositionLeakException` listing every difference.

If `CacheService` had a sloppy inverse that forgot to dispose the cache, this line would
fail the demo instead of leaking silently.

## The public API you actually use

| Type | What it is for |
|---|---|
| `EffectScope` | The primitive without components: `Track` a change and its inverse, `RecoverAsync` runs them LIFO. Start here if you only want RAII with a real undo. |
| `TrackedChange` | The handle `Track` returns. `Dispose` (sync inverse) or `DisposeAsync` (any inverse). Fire at most once, no-op after recovery. |
| `EffectLoop.RunAsync` | Runs a component effect as steps, so an interrupted sequence unwinds what ran. |
| `CompositionHost` | The registry: `Provide`, `Add`, `Retire`, `StateOf`, `FailureOf`, `DisposeAsync`. |
| `IComponent` | What you implement: `Declared` keys plus `Effect`. |
| `IComponentContext` | What your effect receives: `Get` (committed view, declaration-checked), `Provide`, `Add`, `Track`. |
| `FiberState` | `Absent`, `Loading`, `Active`, `Unloading`, `Inactive`, `Failed`. |
| `VerificationHarness` | `Watch`, `Capture`, `Verify`. The registry is always watched; everything else is your call. |

## Write your own component

The whole recipe, in order:

1. Pick or define the key interfaces. A key is just a type; convention says interface.
2. Write the component: declare the keys, and in the effect, make each change through
   `yield return` with an inverse that reverses exactly that change.
3. Register the pieces from the outside: `Provide` for host-level bindings, `Add` for
   components. Order does not matter; parking handles the gaps.
4. When a component should stop providing but stay registered, `Retire` it. When it should
   be gone, dispose its `Add` handle. When everything should be gone, dispose the host.

A component in twenty lines:

```csharp
private interface INameService { string Name { get; } }

private sealed class Greeter(List<string> greetings) : IComponent
{
    public IReadOnlySet<Type> Declared { get; } = new HashSet<Type> { typeof(INameService) };

    public async IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> Effect(IComponentContext ctx)
    {
        yield return () =>
        {
            greetings.Add($"hello {ctx.Get<INameService>().Name}");
            return new ValueTask<Func<ValueTask>>(() =>
            {
                greetings.RemoveAt(greetings.Count - 1);
                return default;
            });
        };
    }
}
```

Add it, provide an `INameService`, and the greeting appears. Retire the name service and the
greeting is gone, removed by the inverse you wrote. Forget the inverse, and the verification
harness is the thing that tells you.

## Rules to remember

- One provider per key. `Provide` on an occupied key replaces, and its undo restores the
  previous binding with its original provider uid.
- The engine is sequential. Every operation is awaited within the call that triggered it, so
  a fiber mid-activation is never observable. There are no thread-safety promises.
- `Get` reads the committed view. You see a new provider only after reactivation, never
  mid-flight.
- Undeclared access is a bug and lands the fiber at `Failed`. A missing dependency is not a
  bug and parks. The two look similar in a stack trace and are opposite in meaning.
- Nothing tracked implicitly. Ambient statics, files, network calls: outside the boundary,
  your undo is the only thing that reverses them. Watch what you care about.

## Where to look next

Each milestone's acceptance criteria live as tests in `tests/CordisSharp.Core.Tests`, named
after the behavior they assert: `EffectScopeTests` for the primitive, `CompositionHostTests`
for the registry, `FiberLifecycleTests` for teardown and failure, `InstantiationAsEffectTests`
for trees, `VerificationHarnessTests` for the harness. If a guarantee in this guide sounds too
good, the test that proves it is one file away.
