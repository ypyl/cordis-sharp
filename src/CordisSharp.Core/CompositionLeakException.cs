namespace CordisSharp.Core;

/// <summary>
/// Thrown by <see cref="VerificationHarness.Verify"/> when the composition did not return to
/// its captured state. One line per difference, all of them at once, so a failing test
/// prints the whole leak list instead of the first.
/// </summary>
public sealed class CompositionLeakException : Exception
{
    public IReadOnlyList<string> Differences { get; }

    public CompositionLeakException(IReadOnlyList<string> differences)
        : base("The composition did not return to its captured state:\n"
            + string.Join("\n", differences.Select(d => "  - " + d)))
        => Differences = differences;
}
