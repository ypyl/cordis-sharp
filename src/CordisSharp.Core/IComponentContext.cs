namespace CordisSharp.Core;

/// <summary>
/// The context a component runs against: its own effect scope, plus the committed view of
/// the bindings it activated against. Access is explicit and declaration-checked.
/// </summary>
public interface IComponentContext : IEffectScope
{
    /// <summary>
    /// Read a declared binding from the committed view. Throws if the key is not declared
    /// by the component, whatever the registry holds; undeclared access is a bug, not a
    /// missing dependency.
    /// </summary>
    T Get<T>() where T : class;

    /// <summary>
    /// Install or replace a binding for the key <typeparamref name="T"/>. The binding is a
    /// tracked change of this component's own activation scope, so deactivating the component
    /// withdraws it, and two-phase teardown makes that withdrawal happen only after its
    /// dependents are done. Provider identity is a fresh uid per call, never reused.
    /// </summary>
    ValueTask<TrackedChange> Provide<T>(T instance) where T : class;
}
