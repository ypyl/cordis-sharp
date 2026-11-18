# 04. The .NET mapping

Short verdict: the core is buildable and most of it is ordinary engineering. One piece fights back, and
that is unloading code.

## What the paper requires, and what .NET has

| Requirement (paper 6.4) | .NET | Verdict |
|---|---|---|
| Closures to hold an inverse and the state it restores | delegates, local functions, `Func<ValueTask>` | free |
| Introduce and retract code at runtime | `AssemblyLoadContext(isCollectible: true)` | possible, with real caveats |
| Typed dependency access, provider extends the context type | nominal interfaces plus a strong-named contract assembly | better than TypeScript |
| Transparent interposition on access | `DispatchProxy`, a decorator, or a source-generated accessor | possible, but see below |
| Metaprogramming to emit typed accessors and the declaration | Roslyn source generators | good fit |

Two TypeScript-specific tricks do not exist here and are not needed:

- **Type-level module augmentation** exists because TypeScript needs a way for a provider module to extend
  the context type. C# has interfaces, so the key is a type and the consumer side is compiler-checked.
- **A runtime `Proxy` with a get trap** is how Cordis resolves `ctx[key]` while checking declarations. The C#
  options are a source generator emitting accessors, or an explicit `ctx.Get<T>()` that walks the fiber chain.

Which also kills most of the paper's section 6.6 for us: interface drift and key collision are largely solved
by assembly identity. A key that is a strong-named interface is not a bare string.

## The five primitives

1. **Closures for the inverse.** `Func<ValueTask>`, `IAsyncDisposable`, `Disposable.Create(...)`.
2. **One mutation primitive holding inverses.** Around 200 lines. LIFO fold, per-step guard, fire at most
   once, a child's disposer folded into its parent's.
3. **Declared keys.** Interfaces as keys plus an explicit declared set per component.
4. **Reactive resolution.** The real work. `Microsoft.Extensions.DependencyInjection` has no removal and no
   notification after build, so this must be a small registry of our own: key to provider, a change signal,
   and a target view keyed by provider identity.
5. **Two-phase teardown with dependents-first waiting.** Per-fiber `TaskCompletionSource`, plus a reverse
   index from provider to dependents derived from the declared sets.

## Shape

```csharp
interface IContext {
    ValueTask<Func<ValueTask>> Effect(Func<ValueTask<Func<ValueTask>>> body);   // change + undo
    IReadOnlySet<Type> Declared { get; }                                        // what I need
    T Get<T>() where T : class;
    ValueTask Provide<T>(T instance) where T : class;
}

sealed class Fiber {
    public long Uid;                              // never reused: the provider identity
    public Fiber? Parent;
    public State State;                           // Loading, Active, Unloading, Inactive, Failed
    public Dictionary<Type, long> Target;          // key -> provider uid
    public Dictionary<Type, long> Committed;       // what it activated against
    public Dictionary<Type, object> Table;         // bindings it installed
    private Func<ValueTask> _accumulator;          // its own inverses
}
```

`IAsyncEnumerable<Func<ValueTask>>` is a faithful stand-in for the effect iterator, and
`CancellationToken` covers abort mid-iteration.

## What .NET gives for free

- Nominal keys, so key collision mostly disappears and versioning becomes a binding decision.
- `IAsyncEnumerable` for the iterator, `CancellationToken` for interruption.
- Roslyn source generators for the declaration and accessors, which is the paper's own suggestion in 6.4 for
  dispensing with runtime interposition.
- **Verification.** The paper's weakest point is unchecked inverses. Here you can snapshot the tracked state
  before loading a component and diff it after unloading, in tests or in debug builds. The guarantee gets an
  actual check behind it, which the reference implementation does not have.

## Prior art in .NET, nearest first

- **MEF.** `[Export]`/`[Import]`, `CompositionContainer`, `DirectoryCatalog`, and recomposition through
  `CompositionBatch` with `AllowRecomposition`. The closest thing .NET has had to spatial composability. It
  never had temporal composability: no inverse tracking, and satisfying imports is not the same as a teardown
  contract. Also effectively legacy.
- **`System.Reactive.Disposables`.** `Disposable.Create` is the inverse closure and `CompositeDisposable`
  disposes in reverse order. The primitive exists and is battle-tested; what is missing is automatic
  registration of every mutation.
- **DI scopes.** The container records the disposables it created and disposes them in reverse order at scope
  end. That is temporal composability for container-created objects only, which is a fraction of the effects
  in a real component.
- **Keyed services (.NET 8+).** The dependency table, without notification, removal, or a notion of "not yet
  provided".
- **`IHostedService` + `IHostApplicationLifetime`.** Process-lifetime granularity: the coarse workaround in a
  hosting abstraction.
- **Orleans grain lifecycle.** Structurally the nearest thing to a fiber, but deactivation is idle-driven
  rather than dependency-driven, a grain that cannot get its dependency fails instead of parking, and a silo
  restart goes back to section 1.2.3.
- **`IFeatureCollection` (ASP.NET Core).** A typed map with presence checks scoped to a request, and nothing
  reacts to changes.

## What fights back

1. **Collectible ALC unloadability.** Unload only succeeds when nothing outside references anything inside.
   Static fields, thread statics, cached delegates, timer callbacks, unresolved task continuations and event
   handlers on long-lived objects all pin it. A fiber's committed view holds references into the context,
   which in .NET means holding references into the ALC. You need weak references or per-ALC proxy types, and
   this is the single biggest risk to a port.
2. **Type identity across reloads.** Two collectible contexts loading the same assembly produce distinct
   `Type` objects, so a store keyed on `Type` cannot see across them. The standard fix is a contract assembly
   in the default context, which weakens the nominal-typing advantage.
3. **Ambient static state.** `DateTime.UtcNow`, `IConfiguration` statics, `HttpClient` defaults, `Console`,
   caches. By the boundary rule these are untracked, which is correct and means a .NET port tracks less than
   a reader would assume.
4. **Teardown concurrency.** The reference implementation starts inverses in reverse order but awaits them
   concurrently. Any inverse that assumes a previously-run inverse completed is a bug there too. Decide
   deliberately: concurrent to match, sequential for predictability.

## No module cache to evict

Node keeps every loaded module in a cache (`require.cache`, internal ESM map) and hot replacement works by
deleting the entry so the next import re-evaluates the file. The process, other modules and their state stay
alive.

.NET has no equivalent. An assembly stays bound to its load context. The only eviction is `Unload()` of a
whole collectible context, and only with no live references. `dotnet watch` hot reload patches method bodies
through metadata updates instead: no new types, no re-running static constructors, no general
re-initialisation, and any cache you own must be cleared by hand via `MetadataUpdateHandler`.

That gap is why the paper's reload maps cleanly to TypeScript and only partially here. It is the one piece
not to port as-is.

## Recommended posture

- Unload **effects**, not **code**. That covers event handlers, timers, hosted services, subscriptions, cache
  entries, connection pools, which is where the real pain is.
- Put contracts in a shared assembly in the default context, so plugin assemblies are implementation only.
- If genuine code eviction is ever needed, isolate in a process. That is the coarse workaround, chosen
  deliberately at the boundary rather than pretended away.
