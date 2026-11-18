namespace CordisSharp.Core;

/// <summary>
/// The composition host: the registry of bindings, the running components, and the root
/// effect scope they all hang from. Providing a binding and adding a component are both
/// tracked effects of the root scope, so disposing the host unwinds the whole composition
/// last-in-first-out: components deactivate before the bindings they depend on are
/// withdrawn.
/// </summary>
public sealed class CompositionHost : IAsyncDisposable
{
    private readonly EffectScope _root = new();
    private readonly Dictionary<Type, Binding> _bindings = new();
    private readonly List<ComponentInstance> _instances = new();
    private long _uidCounter;

    /// <summary>
    /// Install or replace the binding for the key <typeparamref name="T"/>. The provider
    /// identity is a fresh uid per call, never reused, so replacing a provider reactivates
    /// its dependents even when the new value equals the old one. The returned handle's
    /// inverse restores the previous binding with its original uid.
    /// </summary>
    public ValueTask<TrackedChange> Provide<T>(T instance) where T : class =>
        _root.Track(() => ProvideChange(typeof(T), instance));

    /// <summary>
    /// Add a component. If its declared keys are all bound it activates right away;
    /// otherwise it parks. The returned handle's inverse deactivates it and removes it,
    /// and a removed component stops reacting to the registry.
    /// </summary>
    public ValueTask<TrackedChange> Add(IComponent component) =>
        _root.Track(() => AddChange(component));

    /// <summary>Recover the root scope: the whole composition, undone LIFO.</summary>
    public ValueTask DisposeAsync() => _root.RecoverAsync();

    private async ValueTask<Func<ValueTask>> ProvideChange(Type key, object instance)
    {
        var had = _bindings.TryGetValue(key, out var previous);
        var binding = new Binding(instance, checked(++_uidCounter));
        _bindings[key] = binding;

        try
        {
            await Notify(key).ConfigureAwait(false);
        }
        catch
        {
            // A component failed against the new binding. Put the registry back, which
            // deactivates whatever activated, and let the failure surface.
            await RestoreAsync(key, had, previous).ConfigureAwait(false);
            throw;
        }

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
        var instance = new ComponentInstance(spec);
        _instances.Add(instance);

        try
        {
            await ClassifyAsync(instance).ConfigureAwait(false);
        }
        catch
        {
            // Activation failed before the inverse existed; undo the bookkeeping.
            _instances.Remove(instance);
            throw;
        }

        return () => UndoAdd(instance);
    }

    private async ValueTask UndoAdd(ComponentInstance instance)
    {
        await DeactivateAsync(instance).ConfigureAwait(false);
        _instances.Remove(instance);
    }

    /// <summary>
    /// Reclassify every component declaring this key against the current registry:
    /// activating, deactivating, or neutral.
    /// </summary>
    private async ValueTask Notify(Type key)
    {
        foreach (var instance in _instances.Where(i => i.Declares(key)).ToArray())
        {
            await ClassifyAsync(instance).ConfigureAwait(false);
        }
    }

    private async ValueTask ClassifyAsync(ComponentInstance instance)
    {
        var satisfied = instance.Spec.Declared.All(k => _bindings.ContainsKey(k));

        if (!instance.Active)
        {
            if (satisfied)
            {
                await ActivateAsync(instance).ConfigureAwait(false);
            }

            return;
        }

        if (!satisfied)
        {
            await DeactivateAsync(instance).ConfigureAwait(false);
            return;
        }

        var stale = instance.Spec.Declared
            .Any(k => _bindings[k].Uid != instance.Committed![k].Uid);
        if (stale)
        {
            // A provider was replaced, not lost: deactivate against the old committed view,
            // then activate again against the new one.
            await DeactivateAsync(instance).ConfigureAwait(false);
            await ActivateAsync(instance).ConfigureAwait(false);
        }

        // Otherwise neutral: nothing moved for this component.
    }

    private async ValueTask ActivateAsync(ComponentInstance instance)
    {
        var committed = new Dictionary<Type, Binding>(instance.Spec.Declared.Count);
        foreach (var key in instance.Spec.Declared)
        {
            committed[key] = _bindings[key];
        }

        var scope = _root.CreateChild();
        var context = new ComponentContext(instance.Spec, committed, scope);
        instance.Active = true;
        instance.Scope = scope;
        instance.Committed = committed;

        try
        {
            await EffectLoop.RunAsync(scope, instance.Spec.Effect(context)).ConfigureAwait(false);
        }
        catch
        {
            // EffectLoop already unwound the activation scope. Clear the bookkeeping and
            // let the failure surface; siblings are untouched.
            instance.Active = false;
            instance.Scope = null;
            instance.Committed = null;
            throw;
        }
    }

    private async ValueTask DeactivateAsync(ComponentInstance instance)
    {
        if (!instance.Active)
        {
            return;
        }

        instance.Active = false;
        var scope = instance.Scope!;
        instance.Scope = null;

        // Committed stays readable until recovery finishes: a component's teardown may
        // still need the very key it is losing.
        await scope.RecoverAsync().ConfigureAwait(false);
        instance.Committed = null;
    }
}
