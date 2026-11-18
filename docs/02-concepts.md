# 02. Concepts, in plain language

Each entry: what it is, and the one thing it is for. Written after walking the definitions one by one, so
the wording here is deliberately informal.

## The two axes

**Temporal composability** is undoing what a component did when it leaves.
**Spatial composability** is the system knowing what a component needs and reacting when that changes.
Static languages get both cheaply (scope exit, module linking). Neither works once components arrive at
runtime.

## Actions with their undo

**Twisted composition.** A change and its undo travel together as one object. Compose two of them and the
changes go forward while the undos go backward. Analogy: socks then shoes, later shoes off then socks off.
This is the algebra that makes "record as you go, reverse at the end" correct by construction.

**Effect context (`∂Γ`).** The state bundled with one accumulated recovery function. Not a list of undos: all
of them folded into a single lever that returns to the start. Analogy: a shopping trolley with one
return-all button that is rebuilt in front of itself on every item taken. Nesting wraps it again, so each
level keeps its own lever.

**`track`.** The wrapper that takes a plain change plus its inverse and produces the tracked version, which
updates the state and folds the inverse into the accumulator in one indivisible step.

**`recover`.** Run the accumulated lever against the current state and clear it. This is the actual
"remove the component" operation.

**Why the theorems matter.** One says tracking cannot change behaviour (the state moves exactly as it would
without it). One says tracking step by step equals tracking the whole sequence at once. One says recovery
always lands on the start state, *provided the inverses really revert*. That proviso is never checked by the
runtime.

**Effect function and its witness.** An effect that returns, at the moment of application, the inverse that
fits *that* state, rather than one fixed in advance. The witness is the single obligation: the returned
inverse must revert that application. This is the version that can be implemented, because real undos
depend on what is actually there when you act.

**Undo is itself an effect.** Recovering is a change one level up, whose own undo is to redo the change. So
the model is uniform at every level, which is what allows scoped and nested deactivation.

**Effect iterator.** A component performs a sequence, not a single effect, so the sequence is reified: each
step yields the new state, its inverse, and either the next step or a stop signal. It is a delimited
continuation, the thing `yield` gives you, and every iteration boundary is a checkpoint where the partial
accumulator can still be reverted.

## What a component needs

**Coeffect.** What a piece of code requires from its environment, annotated on the context rather than on the
type. Effects are what code does to the world; coeffects are what the world must supply to the code.

**The dependency table and the spec.** The environment is a typed map from keys to values. A component's
specification is just the set of keys it declares. Satisfaction is decidable: all declared keys are bound.

**The classifier.** Each context change is checked against the spec and comes out activating (got what it
needs), deactivating (lost it) or neutral (unrelated). That verdict drives the component's start and stop.
Notably it compares *presence*, not values.

**Why it is reactive without polling.** Every mutation is already an effect with a known inverse, so the
previous domain is always available and "did satisfaction change" is computable at exactly the points where a
change could occur. Reactivity is a corollary of the effect discipline, not a separate subsystem.

**A dependency is a value plus its operations.** Not just a value type: the set of operations allowed on that
binding. This confines access structurally, and it identifies what later has to commute.

**Observational equivalence.** The exact-equality guarantee is an idealisation, because real resources cannot
be restored bit for bit (freeing memory does not restore the heap layout). So all equalities are read as "no
observer can distinguish the states", where an observer runs the key's operations and reads outcomes. This is
the honesty valve that keeps the theory usable.

## Two components, one context

**Independence.** Two components may share a context only if every transformation of one commutes with every
transformation of the other, forward changes and undos alike. If it holds, undo order stops mattering: any
sequence of undos restores the start. If it does not, the paper promises nothing.

**Where it is discharged.** Operations at different keys always commute, so the whole question is within one
key. A dependency is "witnessed" when it ships a proof that its own operations commute, and the obligation
sits with whoever provides the key. A table of entries each with its own unique identifier commutes; an
ordered chain of middleware does not.

**What the decomposition buys.** Effects become the commuting part, revertible in any convenient order.
Coeffects become the order-sensitive part, where order is imposed from outside: within a component by the
accumulator's LIFO, across components by a declared dependency. Composability at the grain of components.

## The runtime objects

**Component.** A triple: declared keys, keys it may provide, effect function.

**Fiber.** One running instance: which component, its parent, its own table, its lifecycle state, its
committed view, its accumulator. The name is borrowed from lightweight-thread terminology and fits: its own
lifecycle inside a larger system, without its own process.

**Registry.** All live fibers. The dependency table of the system is read off it.

**Orchestrator.** Whoever states the desired composition. Deliberately undefined in the calculus: it can only
add a name or raise a retirement flag, never set a lifecycle state.

**Target view vs committed view.** Target is which provider currently backs each declared key; committed is
what the fiber activated against. The lifecycle is a diff of the two. Both record *provider identity*, which
is why a swapped provider is noticed even when the value is identical.

## The lifecycle

Absent, loading, active, unloading, inactive, and failed as an extension.

- Activation walks begin, iterate, finish, stacking inverses.
- Deactivation is two steps by necessity: stop providing, wait until nothing relies on you, then run the
  accumulator. A consumer's teardown may need the very key being withdrawn, as closing a connection pool
  needs the provider.
- Retire and remove are separate operations. Retire only raises a flag; remove requires the flag, an empty
  table and no children. Splitting them is what prevents losing an accumulator, which would leak.
- Only two things can move a fiber: somebody retired it, or the provider behind one of its keys changed.
  Nothing else. Reaction requires a declared key.

## The five results

- **Preservation:** no rule breaks well-formedness.
- **Temporal:** reverting one fiber restores the context and leaves others as they were, given independence.
- **Spatial:** a fiber starts only after its providers, and a provider stops only after its dependents.
- **Progress:** the system always reaches quiet, given acyclic dependencies and finite iterations.
- **Confluence:** where it quiets depends only on the final configuration, not on the schedule.

## The four extensions

**Asynchrony** (hosts cannot abort mid-iteration, so they are inertial), **failure** (raise unwinds partial
state, records the error, does not retry against an unchanged environment, does not take siblings down),
**isolation** (a key paired with its realm is just a larger key set), **configuration** (every revision
decomposes into retire, deactivate, remove, reinsert).
