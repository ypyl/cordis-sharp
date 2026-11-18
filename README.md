# cordis-sharp

A .NET learning project built around one paper:

> **A Programming Paradigm for Spatiotemporal Composability**
> Yifan Shi, Wei Zhang, Tianyi Cui (Peking University, DeepSeek-AI)
> arXiv [2608.25512](https://arxiv.org/abs/2608.25512), 83 pages

The aim is to understand the paradigm by rebuilding it in C#, and to find out which parts survive the
translation. Not a product, not a framework for anyone else, and not a port of the TypeScript reference
implementation.

## Status

**M1 through M5 implemented.** `src/CordisSharp.Core` now has the revertible-effects primitive
(`EffectScope`, `TrackedChange`, `EffectLoop`), the reactive registry (`CompositionHost`, `IComponent`,
`IComponentContext`) and the fiber lifecycle on top: public `FiberState` with `StateOf`/`FailureOf`,
`Retire` separate from removal, component-provided bindings via `ctx.Provide`, two-phase teardown that
runs dependents recursively before the provider's accumulator, and recorded activation failures that
do not fail the triggering call. `ctx.Add` makes a child's existence a tracked change of the parent's
activation scope, so unloading a parent retires children leaves-first with no manager code, and a
reactivated parent instantiates them fresh. `VerificationHarness` snapshots named probes plus the
registry before a cycle and diffs after, so a wrong inverse fails a test instead of leaking silently.
Tests cover all milestones' acceptance cases. M6 is optional and unearned; M7 is time-boxed
investigation.

## How to read this folder

| File | Contents |
|---|---|
| `docs/01-paper-overview.md` | What the paper argues, section by section |
| `docs/02-concepts.md` | The key definitions explained in plain language |
| `docs/03-limits-and-critique.md` | What the paper does not establish, and where it can be argued with |
| `docs/04-dotnet-mapping.md` | What .NET gives for each requirement, and what fights back |
| `docs/05-reference-implementation.md` | What the published TypeScript package actually does |
| `docs/06-plan.md` | Milestones, open decisions, order of work |

## The one-paragraph summary

Software increasingly composes at runtime (plugins, agent harnesses that modify themselves), but the theory
of composition is about compile time. The paper names two missing guarantees: **temporal composability**,
where removing a component completely reverses what it did, and **spatial composability**, where components
declare dependencies and the runtime reactivates them when providers change. It gets both by lifting
**effects** and **coeffects** from type-system ideas into runtime mechanisms, unifies them in a single
context, and proves the guarantees carry from one component to a whole interleaved system. The
implementation is Cordis, a TypeScript meta-framework used by Koishi and its 4000+ plugins.

## Demo

`samples/CordisSharp.Demo` is a console walk through the whole runtime in one session: a component
that parks with no provider, a dependency chain activating in order, a retire that tears dependents
down before the provider's resource is disposed, a parked dependent revived by a fresh provider, a
parent whose children exist as its own recorded effects, a failing component whose error is recorded
while the host keeps running, and the verification harness confirming at the end that nothing leaked.
Run it with `dotnet run --project samples/CordisSharp.Demo`.
A guide to the core library that follows this demo section by section lives at
`samples/CordisSharp.Demo/GUIDE.md`.

## Why this project exists

Two questions, both practical:

1. Is the design small enough to hold in your head, or does it only work because the paper has 83 pages of
   metatheory? The reference implementation suggests it is small: 1,820 lines of core.
2. Which parts of it are possible in .NET, which are awkward, and which are not worth it? The honest answer
   so far: effects and reactive dependencies are ordinary engineering, code unloading is against the grain
   of the CLR, and verification is *easier* here than in TypeScript.
