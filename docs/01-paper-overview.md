# 01. Paper overview

Source: arXiv 2608.25512, 83 pages, read in full for sections 1, 2, 5, 6 and 8; sections 3, 4 and 7 read at
the level of definitions, rule statements and theorem statements.

## The problem

Composition is well understood when it is static: function calls, module imports, class inheritance. Modern
software increasingly composes at runtime: plugin systems, and agent harnesses that generate and replace
their own components while serving requests. For that case there is no theory, only workarounds.

Two dimensions of the problem:

- **Temporal composability.** Removing a component must completely and safely reverse what it did to the
  shared environment: allocations, event registrations, state mutations, child components.
- **Spatial composability.** Components must declare their dependencies and the system must resolve,
  provide, withdraw and re-resolve them as providers come and go, driving activation and deactivation.

In the static setting the first reduces to lexical scoping (RAII, bracket patterns) and the second to module
import resolution. Both reductions depend on the lifetime being a syntactic region and the dependency graph
being fixed at link time. Neither survives a plugin loaded after deployment.

## The workaround, and its price

The OS gives temporal composability at process granularity: kill the process and its state is gone. A
container orchestrator gives spatial composability at service granularity. Using them for a component-level
problem costs:

- process state on every change (caches, connections, in-flight work),
- redundant replicas to keep serving during the restart,
- network and serialisation for calls that could be local function calls,
- no way at all to express dependencies between components sharing one address space.

Their measurement, from the VSCode Marketplace on 2026-06-09: **87 of the top 100 extensions by install count
contain executable code**, so disabling one needs a restart of the whole extension host. Only **7 of the top
100** declare `extensionDependencies` on non-built-in extensions, because the API steers extensions toward
host-provided extension points rather than toward each other.

Worth stating plainly: the coarse mechanism gives **disposability, not reversibility**. A pod can be killed;
the HTTP request it already sent cannot be un-sent. The paper says so itself in 6.1.

## The five contributions

1. **Revertible effects.** Every transformation of the context carries an explicit inverse the runtime
   holds. Tracking and recovery both preserve composition, so the context is recovered on removal.
2. **Reactive coeffects.** A component declares the keys it requires. Every context change is classified
   against that declaration as activating, deactivating or neutral, and the classification drives the
   component's activation and deactivation.
3. **The context paradigm.** Effect context and coeffect context are unified into one context type, with
   every effect and every dependency access mediated through it. The mediation induces an observational
   equivalence under which components do not disturb each other.
4. **A calculus of dynamic composition.** The two mechanisms compose into a notion of component, with an
   operational semantics and a metatheory carrying both guarantees from one component to a system of
   interleaved components.
5. **Cordis**, an implementation: core library, declarative loader with configuration reconciliation, and
   hot module replacement.

## Section map

| Section | Content |
|---|---|
| 1 | Dimensions, motivating failures (VSCode, agent harnesses), the coarse workaround |
| 2 | Preliminaries: effect systems (monadic, algebraic) and coeffect systems (comonadic, graded) |
| 3 | The two runtime mechanisms, unified context, observational equivalence, independence |
| 4 | Calculus: components, fibers, nine rules, five metatheory results, four extensions |
| 5 | Implementation: core library, loader, hot replacement, Koishi case study |
| 6 | Discussion: boundary, multiplexing, access control, language requirements, cycles, versioning |
| 7 | Related work |
| 8 | Conclusion |

## The mechanism in one page

- A single primitive performs every mutation of the context, and the callback yields the inverse of what it
  did. Inverses are folded into one accumulated recovery function, applied last-in-first-out.
- A component is a triple: the keys it requires, the keys it may provide, and its effect function.
- Instantiating a component creates a **fiber**: one instance with its own table, its own accumulator, a
  lifecycle state, a committed view, and a parent.
- A fiber's **target** is the tuple of providers currently backing its declared keys. Its **committed view**
  is the resolution it activated against. The lifecycle is driven by comparing the two, so it reacts to a
  provider being replaced even when the new provider offers equal values.
- Deactivation is two-phase on purpose: stop providing, wait until no other fiber relies on those keys, then
  run the accumulator. A consumer's teardown may still need the key it is losing.
- Instantiation is itself an effect, so a child's existence is recorded as an inverse in the parent's
  accumulator. Unloading a parent therefore retires its children, one level at a time.
- The loader describes the desired system as a persistent configuration tree and reconciles per field, which
  makes hot replacement dispose-and-reinstantiate.
