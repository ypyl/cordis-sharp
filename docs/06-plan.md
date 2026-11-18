# 06. Plan

Nothing here is implemented yet. Each milestone is agreed before it is written.

## Milestones

**M0. Documentation.** This folder. Done.

**M1. Revertible effects only.** One primitive that runs a change and keeps its inverse. Nested scopes,
LIFO recovery, fire-at-most-once, the iterator form. No dependencies, no fibers.
Acceptance: a test that loads three things, disposes out of order, and asserts exactly one reversal per
change; plus a test that interrupting halfway reverses only what ran.
Value: usable immediately anywhere teardown is a hand-written `Dispose`.

**M2. Declared keys and a reactive registry.** Keys as interfaces, an explicit declared set per component, a
registry keyed by provider, and a target view keyed by provider identity. A component whose keys are missing
parks instead of throwing.
Acceptance: component B declares what A provides, B activates only after A, swapping A for a second instance
reactivates B even though the value is equal, and an undeclared access throws.

**M3. Fiber lifecycle and two-phase teardown.** States, inertia, committed view versus target, retire
separate from remove, dependents-first waiting.
Acceptance: unload ordering is observable and asserted; a component that reads its dependency during its own
teardown succeeds.

**M4. Instantiation as an effect.** Parent records "this child exists" as one of its inverses, so unloading a
parent retires children one level at a time.
Acceptance: a three-level tree unloads leaves-first with no manager code.

**M5. Verification harness (a .NET advantage).** Snapshot the tracked state before loading and diff it after
unloading, so a sloppy inverse fails a test instead of leaking silently.
Acceptance: a deliberately wrong inverse is caught.

**M6. Declarative loader and reconciliation.** Desired composition as data, per-field reconciliation.
Optional, and only if a use case earns it.

**M7. Code reload investigation.** Collectible `AssemblyLoadContext`, contract assemblies, type identity
across reloads. Time-boxed and explicitly allowed to conclude "not worth it".

## Decisions to make together

1. **Keys.** Interfaces as keys, string service names as in Cordis, or both? Interfaces give compile-time
   safety; names give late binding and configuration.
2. **Access.** Explicit `ctx.Get<T>()` with a chain walk, or source-generated accessors that keep `ctx.Db`
   ergonomics? The generator is more work and more magic.
3. **Teardown order.** Sequential LIFO for predictability, or concurrent to match the reference
   implementation? Concurrent is faster and considerably harder to reason about.
4. **Cancellation.** One `CancellationToken` per fiber (abort mid-activation), or only between iterations?
   The paper's inertial hosts suggest between iterations.
5. **Multiple providers.** Single provider as in the paper, with a broker for multiplicity, or allow
   collections from the start?
6. **Scope of the dependency table.** Only inter-component services, or also host resources (config, clock,
   filesystem) so they become trackable and swappable?
7. **Nothing tracked implicitly.** Decide up front which ambient statics are accepted as untracked, since the
   boundary rule will silently exclude them.
8. **Error policy.** Park on missing dependency (the paper) or fail fast (what most .NET developers expect)?
   Parking is the point of the design, but it changes how people debug.

## What this project is not

- Not a framework release.
- Not a reimplementation of the loader, realms, or interception before something needs them.
- Not an attempt to make the calculus executable.
