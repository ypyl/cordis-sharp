# 03. Limits, and where the paper can be argued with

Collected while reading, so nothing here gets quoted as if the paper had proven it.

## Stated by the authors themselves

- **The inverse is unchecked.** `ctx.effect` does not verify that the supplied inverse actually reverses the
  effect. The metatheory appeals to it as an author obligation. The coeffect witness is unchecked the same
  way.
- **No performance data.** No benchmark, no overhead figure, no controlled comparison. Their own words: an
  "existence-and-adoption result rather than a quantitative one". Measuring abstraction overhead and its
  effect on developer productivity against a baseline is future work.
- **One ecosystem, one language.** Koishi, 4000+ community plugins over four years, plus a second independent
  application (its web console). That cannot separate the paradigm from the TypeScript realisation or from
  the chatbot domain.
- **Koishi runs the previous version.** It uses Cordis v3; the paper presents v4, which refines the effect and
  coeffect semantics and redesigns the loader.

## Consequences of the design

- **Cycles become permanent inactivity.** Two components that need each other's keys never activate. Detectable
  statically, but only resolved by decomposition into integration components, which grows quadratically with
  the number of interacting components.
- **Versioning is unsolved.** Linking by bare key name gives interface drift and key collision. The three
  answers (namespacing, package-manager peer dependencies, structural compatibility) each fail somewhere. The
  authors call a unified model an open problem.
- **The boundary is a judgement call.** Inside means the system alone writes the location and can restore it.
  Everything else acts as identity and is untracked, which quietly excludes a great deal of real state.
- **Emissions cannot be reverted.** Once an operation sends data outside the boundary, only withholding or
  compensation remain, and compensation does not inherit the composition proof.
- **One provider per key.** Multiplicity needs a broker, which turns into load balancing, rolling updates and
  cross-process calls. Cheap to describe, real work to build.
- **Composability is bought with isolation.** An in-process component shares a fault and memory boundary with
  its host. Untrusted code still needs an external sandbox.

## Assertions that are weaker than they read

1. **"Rebuilding it takes seconds to minutes."** Cited to Armstrong's 2003 Erlang thesis, not measured on any
   system resembling the reader's.
2. **"Modern systems increasingly compose at a finer level."** Uncited, and it carries the whole motivation.
3. **"Reduces to lexical scoping."** True for resources acquired through a scoped handle. A cache entry, a
   registry row, a written file or a subscribed event handler is not undone by leaving a block.
4. **The static case is stricter, not merely a special case.** The compiler enforces the pairing in RAII; the
   runtime here trusts the author. That is a downgrade the summary sentence hides.
5. **"Lexical" is not free even statically.** Non-local exits break naive pairing, which is why `try/finally`
   exists and why the cited bracket paper is about asynchronous exceptions.

## The economic test the paper never states

The fine-grained mechanism pays when

```
removal frequency  x  state-rebuild cost  >  cost of maintaining the discipline
```

For most business systems removal happens monthly and the coarse mechanism is simply correct. The argument
bites where the paper's own section 1.2.2 says it does: continuous, unsupervised self-modification.

## Where this project lands

Take three things and leave the rest: the action-plus-inverse primitive, the provider-identity re-resolution
test, and absent dependency means parked rather than failed. Skip the calculus, the realm machinery, and
code unloading until something forces them.
