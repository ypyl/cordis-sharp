namespace CordisSharp.Core;

/// <summary>
/// The per-activation context handed to a component's effect. Get reads the committed view,
/// the snapshot taken at activation, so a provider swapped in later stays invisible until
/// the component is deactivated and reactivated. Provide installs a binding as a tracked
/// change of this activation scope.
/// </summary>
internal sealed class ComponentContext : IComponentContext
{
    private readonly IComponent _spec;
    private readonly Dictionary<Type, Binding> _committed;
    private readonly EffectScope _scope;
    private readonly CompositionHost _host;
    private readonly Fiber _fiber;

    internal ComponentContext(
        IComponent spec,
        Dictionary<Type, Binding> committed,
        EffectScope scope,
        CompositionHost host,
        Fiber fiber)
    {
        _spec = spec;
        _committed = committed;
        _scope = scope;
        _host = host;
        _fiber = fiber;
    }

    public T Get<T>() where T : class
    {
        var key = typeof(T);
        if (!_spec.Declared.Contains(key))
        {
            throw new InvalidOperationException(
                $"'{key.Name}' is not declared by this component. Declare it to access it.");
        }

        return (T)_committed[key].Value;
    }

    public ValueTask<TrackedChange> Provide<T>(T instance) where T : class =>
        _scope.Track(() => _host.ProvideChange(typeof(T), instance!, _fiber));

    public ValueTask<TrackedChange> Track(Func<ValueTask<Func<ValueTask>>> change) =>
        _scope.Track(change);

    /// <summary>Recover the component's own activation scope, not the whole host.</summary>
    public ValueTask DisposeAsync() => _scope.DisposeAsync();
}
