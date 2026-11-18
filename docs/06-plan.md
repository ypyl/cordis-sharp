# 06. Plan

Nothing here is implemented yet. Each milestone is agreed before it is written.

## Milestones

**M0. Documentation.** This folder. Done.

**M1. Revertible effects only.** One primitive that runs a change and keeps its inverse. Nested scopes,
LIFO recovery, fire-at-most-once, the iterator form. No dependencies, no fibers.
Acceptance: a test that loads three things, disposes out of order, and asserts exactly one reversal per
change; plus a test that interrupting halfway reverses only what ran.
Value: usable immediately anywhere teardown is a hand-written `Dispose`.
**Done.** `EffectScope`, `TrackedChange`, `EffectLoop`, both acceptance tests plus edge cases. The child
fold already retires grandchildren one level at a time, so M4's mechanism exists here for scopes and
only needs to be attached to fibers.

**M2. Declared keys and a reactive registry.** Keys as interfaces, an explicit declared set per component, a
registry keyed by provider, and a target view keyed by provider identity. A component whose keys are missing
parks instead of throwing.
Acceptance: component B declares what A provides, B activates only after A, swapping A for a second instance
reactivates B even though the value is equal, and an undeclared access throws.
**Done.** `CompositionHost`, `IComponent`, `IComponentContext`; keys are interfaces, provider identity
is a fresh uid per `Provide`, and both `Provide` and `Add` are tracked effects of the root scope, so host
disposal already unwinds dependents before providers. One known simplification: a provider withdrawn by
its own handle deactivates dependents after the binding is gone, which is exactly what M3's two-phase
teardown exists to fix.

**M3. Fiber lifecycle and two-phase teardown.** States, inertia, committed view versus target, retire
separate from remove, dependents-first waiting.
Acceptance: unload ordering is observable and asserted; a component that reads its dependency during its own
teardown succeeds.
**Done.** `FiberState` is public and observable via `StateOf`/`FailureOf`; `Retire` does two-phase teardown but
keeps the fiber registered, removal stays the inverse of `Add`. `IComponentContext.Provide` makes a fiber a
provider: its bindings are tracked changes of its own activation scope, so deactivation withdraws them after
dependents have gone, recursively. Inertia needed no machinery: the engine is sequential and awaited, so a
fiber mid-Loading is never observable. Activation failure is recorded on the fiber and does not fail the
calling `Add`/`Provide`; a failed fiber retries only when the registry actually changes, which is the paper's
failure extension exactly.

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
Acceptance: the unload is *observed*, not assumed. Keep a `WeakReference` to the context, force collections,
and assert it died. A collectible ALC that silently never collects is indistinguishable from success
unless checked.

## Decisions to make together

1. **Keys.** Interfaces as keys, string service names as in Cordis, or both? Interfaces give compile-time
   safety; names give late binding and configuration.
   **Decided for M2: interfaces only.** String names can be added later as a wrapper; the reverse direction
   would be a breaking change.
2. **Access.** Explicit `ctx.Get<T>()` with a chain walk, or source-generated accessors that keep `ctx.Db`
   ergonomics? The generator is more work and more magic.
   **Decided for M2: explicit `Get`.** The generator stays possible later; it is ergonomics, not semantics.
3. **Teardown order.** Sequential LIFO for predictability, or concurrent to match the reference
   implementation? Concurrent is faster and considerably harder to reason about.
   **Decided for M1: sequential LIFO.**
4. **Cancellation.** One `CancellationToken` per fiber (abort mid-activation), or only between iterations?
   The paper's inertial hosts suggest between iterations.
   **Decided for M1: between iterations only.**
5. **Multiple providers.** Single provider as in the paper, with a broker for multiplicity, or allow
   collections from the start?
6. **Scope of the dependency table.** Only inter-component services, or also host resources (config, clock,
   filesystem) so they become trackable and swappable?
7. **Nothing tracked implicitly.** Decide up front which ambient statics are accepted as untracked, since the
   boundary rule will silently exclude them.
8. **Error policy.** Park on missing dependency (the paper) or fail fast (what most .NET developers expect)?
   Parking is the point of the design, but it changes how people debug.
   **Decided for M2: park.** Undeclared access still throws; that is a bug, not a missing dependency.

## What this project is not

- Not a framework release.
- Not a reimplementation of the loader, realms, or interception before something needs them.
- Not an attempt to make the calculus executable.
