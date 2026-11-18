namespace CordisSharp.Core;

/// <summary>An opaque capture: every probe's value plus the registry view, at one moment.
/// Hand it back to <see cref="VerificationHarness.Verify"/> after the composition is done.</summary>
public sealed class Snapshot
{
    internal IReadOnlyList<(string Name, object? Value, IProbe Probe)> ProbeValues { get; }

    internal IReadOnlyList<(Type Key, object Value, long Uid)> Registry { get; }

    internal Snapshot(
        IReadOnlyList<(string Name, object? Value, IProbe Probe)> probeValues,
        IReadOnlyList<(Type Key, object Value, long Uid)> registry)
    {
        ProbeValues = probeValues;
        Registry = registry;
    }
}
