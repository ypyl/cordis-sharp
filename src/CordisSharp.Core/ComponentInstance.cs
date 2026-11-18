namespace CordisSharp.Core;

/// <summary>
/// One component in the registry. The committed view is the snapshot of bindings the
/// component activated against; the classifier compares it against the current bindings
/// to decide activating, deactivating or neutral.
/// </summary>
internal sealed class ComponentInstance(IComponent spec)
{
    public IComponent Spec { get; } = spec;

    public bool Active { get; set; }

    public EffectScope? Scope { get; set; }

    public Dictionary<Type, Binding>? Committed { get; set; }

    public bool Declares(Type key) => Spec.Declared.Contains(key);
}
