# 05. What the published package actually does

Checked on 2026-11-14 against `github.com/cordiverse/cordis`.

- Package `cordis`, version **4.0.0-rc.13**, monorepo of 17 packages.
- Packages: `core`, `loader`, `hmr`, `group`, `include`, `timer`, `server`, `http`, `http-socks`, `fs`,
  `fs-node`, `logger-console`, `cli`, `cli-cordis`, `create`, `env`, `package-manager`.
- Core is **1,820 lines**: `fiber.ts` 557, `reflect.ts` 324, `utils.ts` 298, `registry.ts` 253, `events.ts`
  222, `service.ts` 83, `context.ts` 76.

The paper describes v4; Koishi runs v3. So the artifact and the evidence are one version apart, which the
paper's footnote says openly.

## Where the code differs from the paper

| Paper | Code |
|---|---|
| keys are names | keys are service names resolved off the context prototype, filled by `@Inject` |
| inverses run LIFO | they are *started* in reverse order but awaited concurrently (`Promise.all(disposables.clear().reverse().map(...))`) |
| five lifecycle states as drawn in Figure 1 | `PENDING`, `ACTIVE`, `FAILED`, `DISPOSED`, with the state derived lazily from `uid`, error and `inertia` rather than stored by rule |
| one provider per key | enforced per service, plus `registry.replace()` with alias forwarding for hot swap |
| a fiber's target is a tuple of provider uids | `committed` is a plain dictionary keyed by service name; freshness comes from `uid`, a monotonic counter that is never reused |

## Five implementation tricks worth copying

1. **The disposer is both callable and awaitable.** `effect()` returns something you can invoke to undo, or
   `await` to know the undo finished. In C# this is a small type implementing both `IDisposable` and
   `IAsyncDisposable`; async-only loses the synchronous path.
2. **An epoch flag per effect.** Gives fire-at-most-once and discards late arrivals once the fiber has left.
3. **Nested effects reparent themselves.** An inner effect that supersedes an outer one is removed from the
   parent list by its node handle, so the grandparent does not also undo it.
4. **The disposables list preserves insertion order with O(1) middle deletion**, backed by weak keys. In C#
   that is `LinkedList<T>` plus the node handle; `List.Remove` is O(n) and it will be felt.
5. **Instantiation is literally a parent effect.** `this.dispose = parent.fiber.effect(() => {...}, 'ctx.use()')`.
   That line is Definition 52 of the paper, in one line of TypeScript.

## What does not translate

- **Prototype-chain contexts.** `ctx.extend()` builds a child by prototypal inheritance, so isolation and
  interception are cheap copies. C# has nothing of the kind: use a parent pointer plus lookup, or generated
  accessors.
- **The `Proxy` get trap.** `ctx[key]` with declaration checking at the trap is the only feature with no clean
  C# equivalent. Either a source generator emits accessors bound to declared slots, or callers say
  `ctx.Get<T>()` and the runtime walks the chain. The explicit call is the honest port: same guarantees, less
  ergonomics.

## The conclusion this gives us

The design is small. 1,820 lines of core, with the only clever device being the `Proxy`. A C# version of the
effects and the registry is a weekend of work; the lifecycle, loader and reconciliation are two or three
weeks of evenings; code unloading is the one part that stays genuinely hard.
