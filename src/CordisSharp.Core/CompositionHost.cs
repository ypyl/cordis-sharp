namespace CordisSharp.Core;

/// <summary>
/// The composition host: the registry of bindings, the running fibers, and the root effect
/// scope they all hang from. Providing a binding and adding a component are both tracked
/// effects of the root scope, so disposing the host unwinds the whole composition
/// last-in-first-out: fibers unload before the bindings they depend on are withdrawn.
///
/// Teardown is two-phase on purpose. Deactivating a fiber first deactivates, recursively,
/// every fiber committed to a binding it provides, and only then runs its accumulator. A
/// dependent's teardown therefore happens while the bindings and the resources behind them
/// are still live: closing a connection pool needs the pool to still be there.
///
/// An activation failure is recorded on the fiber and does not fail the triggering call.
/// The classifier only runs when the registry changes, so a failed fiber retries on a new
/// environment and never against an unchanged one.
/// </summary>
public sealed class CompositionHost : IAsyncDisposable
{
    private readonly EffectScope _root = new();
    private readonly Dictionary<Type, Binding> _bindings = new();
    private readonly List<Fiber> _instances = new();
    private long _uidCounter;

    /// <summary>
    /// Install or replace a host-level binding for the key <typeparamref name="T"/>. The
    /// provider identity is a fresh uid per call, never reused, so replacing a provider
    /// reactivates its dependents even when the new value equals the old one. The returned
    /// handle's inverse restores the previous binding with its original uid.
    /// </summary>
    public ValueTask<TrackedChange> Provide<T>(T instance) where T : class =>
        _root.Track(() => ProvideChange(typeof(T), instance, provider: null));

    /// <summary>
    /// Add a component. If its declared keys are all bound it activates right away;
    /// otherwise it parks. The returned handle's inverse deactivates it and removes it,
    /// and a removed fiber stops reacting to the registry.
    /// </summary>
    public ValueTask<TrackedChange> Add(IComponent component) =>
        _root.Track(() => AddChange(component));

    /// <summary>
    /// Retire a fiber: two-phase teardown, its dependents first, then its own accumulator.
    /// A retired fiber stops providing and never reactivates, but it stays in the registry:
    /// only undoing its Add, or disposing the host, removes it.
    /// </summary>
    public async ValueTask Retire(IComponent component)
    {
        var fiber = Find(component) ?? throw new InvalidOperationException(
            "The component is not part of this composition.");
        fiber.Retired = true;
        await DeactivateAsync(fiber).ConfigureAwait(false);
    }

    /// <summary>The fiber's state, or <see cref="FiberState.Absent"/> if the component is
    /// not registered here.</summary>
    public FiberState StateOf(IComponent component) => Find(component)?.State ?? FiberState.Absent;

    /// <summary>The error recorded by the last failed activation attempt, if any.</summary>
    public Exception? FailureOf(IComponent component) => Find(component)?.Failure;

    /// <summary>Recover the root scope: the whole composition, undone LIFO.</summary>
    public ValueTask DisposeAsync() => _root.RecoverAsync();

    private Fiber? Find(IComponent component) => _instances.FirstOrDefault(f => f.Spec == component);

    /// <summary>
    /// Install or replace a binding. Called from the root scope for host-level provides
    /// and from a fiber's own activation scope for component provides, so that a fiber's
    /// accumulator withdraws its bindings when it deactivates.
    /// </summary>
    internal async ValueTask<Func<ValueTask>> ProvideChange(Type key, object instance, Fiber? provider)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var had = _bindings.TryGetValue(key, out var previous);
        var binding = new Binding(instance, checked(++_uidCounter), provider);
        _bindings[key] = binding;

        await Notify(key).ConfigureAwait(false);

        return () => UndoProvide(key, binding, had, previous);
    }

    private async ValueTask UndoProvide(Type key, Binding binding, bool had, Binding previous)
    {
        // Only restore if the binding is still ours. A newer provider's inverse runs first
        // in LIFO order and has handled theirs; restoring over it would clobber the newer one.
        if (_bindings.TryGetValue(key, out var current) && current.Uid == binding.Uid)
        {
            await RestoreAsync(key, had, previous).ConfigureAwait(false);
        }
    }

    private async ValueTask RestoreAsync(Type key, bool had, Binding previous)
    {
        if (had)
        {
            _bindings[key] = previous;
        }
        else
        {
            _bindings.Remove(key);
        }

        await Notify(key).ConfigureAwait(false);
    }

    private async ValueTask<Func<ValueTask>> AddChange(IComponent spec)
    {
        var fiber = new Fiber(spec);
        _instances.Add(fiber);
        await ClassifyAsync(fiber).ConfigureAwait(false);
        return () => UndoAdd(fiber);
    }

    private async ValueTask UndoAdd(Fiber fiber)
    {
        await DeactivateAsync(fiber).ConfigureAwait(false);
        _instances.Remove(fiber);
    }

    /// <summary>
    /// Reclassify every fiber declaring this key against the current registry:
    /// activating, deactivating, or neutral.
    /// </summary>
    private async ValueTask Notify(Type key)
    {
        foreach (var fiber in _instances.Where(f => f.Declares(key)).ToArray())
        {
            await ClassifyAsync(fiber).ConfigureAwait(false);
        }
    }

    private async ValueTask ClassifyAsync(Fiber fiber)
    {
        if (fiber.Retired)
        {
            return;
        }

        var satisfied = fiber.Spec.Declared.All(k => _bindings.ContainsKey(k));

        if (fiber.State != FiberState.Active)
        {
            // Parked, or failed: satisfied means try (again). The classifier only runs on a
            // registry change, so a failed fiber retries on a new environment, never on a
            // false alarm.
            if (satisfied)
            {
                await ActivateAsync(fiber).ConfigureAwait(false);
            }

            return;
        }

        if (!satisfied)
        {
            await DeactivateAsync(fiber).ConfigureAwait(false);
            return;
        }

        var stale = fiber.Spec.Declared.Any(k => _bindings[k].Uid != fiber.Committed![k].Uid);
        if (stale)
        {
            // A provider was replaced, not lost: deactivate against the old committed view,
            // then activate again against the new one.
            await DeactivateAsync(fiber).ConfigureAwait(false);
            await ActivateAsync(fiber).ConfigureAwait(false);
        }

        // Otherwise neutral: nothing moved for this fiber.
    }

    private async ValueTask ActivateAsync(Fiber fiber)
    {
        var committed = new Dictionary<Type, Binding>(fiber.Spec.Declared.Count);
        foreach (var key in fiber.Spec.Declared)
        {
            committed[key] = _bindings[key];
        }

        var scope = _root.CreateChild();
        var context = new ComponentContext(fiber.Spec, committed, scope, this, fiber);
        fiber.State = FiberState.Loading;
        fiber.Scope = scope;
        fiber.Committed = committed;
        fiber.Failure = null;

        try
        {
            await EffectLoop.RunAsync(scope, fiber.Spec.Effect(context)).ConfigureAwait(false);
            fiber.State = FiberState.Active;
        }
        catch (Exception error)
        {
            // The failure extension: record the error and leave the fiber failed. The
            // triggering call completes, siblings are untouched, and EffectLoop already
            // unwound the scope, so whatever ran is reversed and the fiber's bindings,
            // if it got that far, are withdrawn.
            fiber.State = FiberState.Failed;
            fiber.Scope = null;
            fiber.Committed = null;
            fiber.Failure = error;
        }
    }

    private async ValueTask DeactivateAsync(Fiber fiber)
    {
        if (fiber.State != FiberState.Active)
        {
            return;
        }

        fiber.State = FiberState.Unloading;

        // Phase one: stop providing means waiting. Every fiber committed to a binding this
        // one provides deactivates first, recursively, while the bindings and the resources
        // behind them are still live.
        foreach (var pair in _bindings.Where(b => b.Value.Provider == fiber).ToArray())
        {
            var dependents = _instances
                .Where(f => f.Declares(pair.Key)
                         && f.Committed is not null
                         && f.Committed[pair.Key].Uid == pair.Value.Uid)
                .ToArray();
            foreach (var dependent in dependents)
            {
                await DeactivateAsync(dependent).ConfigureAwait(false);
            }
        }

        // Phase two: nobody relies on this fiber anymore. Run its accumulator, which
        // withdraws its bindings and reverses everything it did.
        var scope = fiber.Scope!;
        fiber.Scope = null;

        // Committed stays readable until recovery finishes: a component's teardown may
        // still need the very key it is losing.
        await scope.RecoverAsync().ConfigureAwait(false);
        fiber.Committed = null;
        fiber.State = FiberState.Inactive;
    }
}
