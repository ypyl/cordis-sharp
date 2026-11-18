namespace CordisSharp.Core;

/// <summary>
/// A component: the keys it requires, and its effect. Keys are interfaces by convention;
/// the runtime treats any type as a key and compares presence, not values.
/// </summary>
public interface IComponent
{
    /// <summary>
    /// The keys this component requires. A declared key with no binding parks the component:
    /// it activates when every declared key is bound, and no exception is thrown meanwhile.
    /// </summary>
    IReadOnlySet<Type> Declared { get; }

    /// <summary>The component's effect, reified as steps so each yield is a checkpoint.</summary>
    IAsyncEnumerable<Func<ValueTask<Func<ValueTask>>>> Effect(IComponentContext context);
}
