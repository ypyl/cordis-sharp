namespace CordisSharp.Core;

/// <summary>
/// The lifecycle of one fiber, observable from the host. Transitions happen only in the
/// single sequential flow of the engine, so a fiber mid-transition is never visible to
/// anything that could act on it: the inertial host of the paper, for free.
/// </summary>
public enum FiberState
{
    /// <summary>Not part of this composition.</summary>
    Absent,

    /// <summary>Activation is running. Cannot be interrupted mid-activation.</summary>
    Loading,

    /// <summary>Activated, committed against its target view.</summary>
    Active,

    /// <summary>
    /// Two-phase teardown in progress: dependents are going first, then the accumulator.
    /// </summary>
    Unloading,

    /// <summary>Parked: keys are missing, or the fiber is retired. No error.</summary>
    Inactive,

    /// <summary>
    /// The last activation attempt threw and the error is recorded. The classifier only
    /// runs when the registry actually changes, so the fiber retries on a new environment,
    /// never against an unchanged one.
    /// </summary>
    Failed,
}
