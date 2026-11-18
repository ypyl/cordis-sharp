namespace CordisSharp.Core;

/// <summary>
/// One fiber: a component instance with a lifecycle, a committed view of the bindings it
/// activated against, and its own activation scope holding everything it did, including
/// the bindings it provides.
/// </summary>
internal sealed class Fiber(IComponent spec)
{
    public IComponent Spec { get; } = spec;

    public FiberState State { get; set; } = FiberState.Inactive;

    /// <summary>Retired: two-phase teardown has run or is running, and it never reactivates.</summary>
    public bool Retired { get; set; }

    /// <summary>The error recorded by the last failed activation, if any.</summary>
    public Exception? Failure { get; set; }

    public EffectScope? Scope { get; set; }

    /// <summary>The snapshot this fiber activated against, kept until teardown finishes.</summary>
    public Dictionary<Type, Binding>? Committed { get; set; }

    public bool Declares(Type key) => Spec.Declared.Contains(key);
}
