namespace CordisSharp.Core;

/// <summary>
/// The verification harness: the check the reference implementation does not have. The paper's
/// runtime never verifies that an inverse actually reverses its change; here, a test can make
/// a sloppy inverse fail loudly instead of leaking silently.
///
/// The harness does not guess what counts as tracked state. You name the pieces of the
/// environment you care about with probes; whatever you do not watch is untracked, which
/// makes the "nothing tracked implicitly" boundary visible instead of implied. The registry
/// is always watched, because the host owns it.
/// </summary>
public sealed class VerificationHarness
{
    private readonly CompositionHost _host;
    private readonly List<IProbe> _probes = [];

    public VerificationHarness(CompositionHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>
    /// Watch a named piece of the environment: a function that reads it, compared by
    /// equality. The comparison is the whole contract: a change your probe cannot see is
    /// a change this harness cannot catch.
    /// </summary>
    public void Watch<T>(string name, Func<T> probe, IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(probe);
        _probes.Add(new Probe<T>(name, probe, comparer ?? EqualityComparer<T>.Default));
    }

    /// <summary>Read every probe and the registry, and hold the values.</summary>
    public Snapshot Capture() => new(
        _probes.Select(p => (p.Name, p.Capture(), p)).ToList(),
        _host.RegistryView());

    /// <summary>
    /// Compare the current state against a snapshot. Throws once, listing every difference.
    /// </summary>
    public void Verify(Snapshot before)
    {
        ArgumentNullException.ThrowIfNull(before);
        var differences = new List<string>();

        var live = _probes.ToDictionary(p => p.Name);
        foreach (var (name, captured, probe) in before.ProbeValues)
        {
            if (!live.TryGetValue(name, out var currentProbe))
            {
                differences.Add($"{name}: unwatched before this verification");
                continue;
            }

            var current = currentProbe.Capture();
            if (!currentProbe.ValuesEqual(captured, current))
            {
                differences.Add(
                    $"{name}: expected {currentProbe.Describe(captured)}, found {currentProbe.Describe(current)}");
            }
        }

        var beforeRegistry = before.Registry.ToDictionary(r => r.Key);
        var afterRegistry = _host.RegistryView().ToDictionary(r => r.Key);

        foreach (var (key, entry) in afterRegistry)
        {
            if (!beforeRegistry.TryGetValue(key, out var prior))
            {
                differences.Add($"registry: {key.Name} still bound (was unbound at capture)");
            }
            else if (prior.Uid != entry.Uid)
            {
                differences.Add(
                    $"registry: {key.Name} rebound by provider uid {entry.Uid} (expected uid {prior.Uid})");
            }
            else if (!Equals(prior.Value, entry.Value))
            {
                differences.Add(
                    $"registry: {key.Name} changed value (expected {prior.Value}, found {entry.Value})");
            }
        }

        foreach (var (key, _) in beforeRegistry.Where(kv => !afterRegistry.ContainsKey(kv.Key)))
        {
            differences.Add($"registry: {key.Name} missing (was bound at capture)");
        }

        if (differences.Count > 0)
        {
            throw new CompositionLeakException(differences);
        }
    }
}

/// <summary>A named probe: reads a piece of the environment and compares two readings.</summary>
internal interface IProbe
{
    string Name { get; }

    object? Capture();

    bool ValuesEqual(object? before, object? after);

    string Describe(object? value);
}

internal sealed class Probe<T>(string name, Func<T> probe, IEqualityComparer<T> comparer) : IProbe
{
    public string Name => name;

    public object? Capture() => probe();

    public bool ValuesEqual(object? before, object? after) =>
        comparer.Equals((T)before!, (T)after!);

    public string Describe(object? value) =>
        value is null ? "null" : value.ToString() ?? value.GetType().Name;
}
