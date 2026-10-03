# AGENTS.md

Guidance for AI agents (and humans) working in this repository.

## What this is

The development project of the Unity package `com.pragma.dotslockstep` (Pragma DOTS Lockstep): deterministic lockstep
for Entities on top of Netcode for Entities. The package is `Assets/PragmaDOTSLockstep` and is consumed through a git
URL with `?path=Assets/PragmaDOTSLockstep`; everything else in the project (tests, the sample, research notes) is not
shipped. Editor version: `ProjectSettings/ProjectVersion.txt` (Unity 6000.6, Entities 6.6, Netcode for Entities 6.6).

The package is unreleased: backward compatibility is not required yet, prefer clean renames over compatibility shims.

## Layout

| Path | Contents |
|------|----------|
| `Assets/PragmaDOTSLockstep/Runtime/Mathematics` | `Pragma.Lockstep.Mathematics`: `FixedPoint`, `FixedVector2`, `FixedVector3`, `FixedQuaternion`, `FixedMath`, `FixedRandom`. No Entities dependency |
| `Assets/PragmaDOTSLockstep/Runtime/Core` | Transport-agnostic protocol: `LockstepServer`, `LockstepClient`, settings, framing, frame history, replays, loopback network |
| `Assets/PragmaDOTSLockstep/Runtime/Simulation` | `LockstepSimulation` (the isolated world), the system group, command buffer systems, frame application, checksums, entity ids |
| `Assets/PragmaDOTSLockstep/Runtime/Simulation/Components` | Simulation components and singletons |
| `Assets/PragmaDOTSLockstep/Runtime/Transforms` | `LockstepTransform`, history and interpolation |
| `Assets/PragmaDOTSLockstep/Runtime/Client` | Local input, `LockstepWorlds`, offline mode, prefab registry copy, `LockstepViewSystem` |
| `Assets/PragmaDOTSLockstep/Runtime/Views` | `Pragma.Lockstep.Views`: GameObject views ported from ECV (DawnOfWar): `EntityView`, view parts, `EntityViewKey`, `EntityViewManagerSystem`, update systems, catalogs, `IEntityViewPool` |
| `Assets/PragmaDOTSLockstep/Runtime/Netcode` | `Pragma.Lockstep.Netcode`: the RPC and the Netcode server and client systems |
| `Assets/PragmaDOTSLockstep/Runtime/Netcode/Components` | Server/client config, status and start/end request components |
| `Assets/PragmaDOTSLockstep/Runtime/Authoring` | `Pragma.Lockstep.Authoring`: bakers |
| `Assets/PragmaDOTSLockstep/Editor` | Fixed-point drawers, *Window > Pragma > Lockstep Sessions* |
| `Assets/PragmaDOTSLockstep/Skills~` | Claude Code skills shipped with the package (gameplay, sessions, desync); Unity skips the folder. Read them before writing simulation code |
| `Assets/Tests/Editor` | `Pragma.Lockstep.Tests.Editor`: the EditMode suite (not shipped); helpers and test components in `Support` |
| `Assets/Tests/Runtime` | `Pragma.Lockstep.Tests.Runtime`: MonoBehaviours the tests add to GameObjects; Unity refuses components from Editor assemblies |
| `Assets/Examples` | *Lockstep Arena* sample (not shipped); its systems run in every simulation world of this project |
| `documentation.md`, `mebecs_research.md`, `photon_research.md` | Design notes and the research they are based on |

## Architecture invariants

- **The server never simulates.** `LockstepServer` orders input: it closes tick `T` when its clock reaches `T + 1`,
  repeats a late player's previous input, moves late commands to the next tick (never drops them) and puts joins and
  leaves into the frames. Anything that needs simulation state on the server is a design change.
- **The simulation world is isolated.** Only the lockstep systems write to it. Systems get in through
  `LockstepSimulationSystemGroup`, whose custom filter bit `LockstepWorldFilter.SIMULATION` (`1 << 28`) they inherit.
  System creation order is sorted (by full name, then `TypeManager.SortSystemTypesInCreationOrder`) and must never
  depend on discovery order.
- **Entity ids are process-global.** Entities 6.x allocates `Entity` index/version from one store shared by every world
  of the process, so the same simulated entity has different ids on different machines (and in two worlds of one
  process). Never hash, sort, serialize or send `Entity` values. The checksum identifies entities by traversal
  position and replaces `Entity` fields with that position; `LockstepEntityId` is the cross-client identity.
- **The checksum** (`LockstepChecksum`) hashes field bytes only (padding, pointers and blob references skipped),
  visits archetypes in creation order and component types by stable type hash, masks enableable bits, and skips
  managed, shared and chunk components and `[LockstepChecksumIgnore]`. Any change to it changes every hash and
  invalidates the checksums stored in replays.
- **Math is part of the protocol.** `FixedPoint` and `FixedMath` are integer-only and bit-identical in Mono, IL2CPP and Burst.
  Changing a result (rounding, series, constants) changes `FixedPointTests.GOLDEN_HASH`, breaks old replays and
  desyncs mixed builds; do it only on purpose and record it in the CHANGELOG.
- **Wire format.** Any change to a message bumps `LockstepProtocol.VERSION`; the replay format has its own
  `FORMAT_VERSION`. `LockstepSimulation.DefaultSimulationHash` covers the protocol version and the simulation system
  list, which is what `ValidateSimulationHash` compares.
- **Netcode integration** is one hand-serialized RPC (`LockstepPacketRpc`, 1024 bytes) on the reliable channel; the
  framer fragments larger messages. Connections are tracked by entity, not by `NetworkId`.
- **No managed components** (deprecated in Entities 6.6): running sessions are found through the static
  `LockstepWorlds` registry keyed by `World.SequenceNumber`.
- **The presentation never writes to the simulation world**, not even a tag or a cleanup component: it would move the
  entity to another archetype, change the chunk order, the hash and the `LockstepEntityId` order. GameObject views find
  lifetimes by comparing keyed entities with their views and changes by chunk change versions;
  `EntityViewTests.Views_LeaveTheSimulationUntouched` guards it. Local state (selection, hover) stays on the views.
- **Burst.** Burst-compiled code must not allocate managed arrays (archetypes use `stackalloc ComponentType[] { ... }`).
  Burst falls back to managed code with only a console error, so tests still pass: check the console for
  "Burst error" after touching Burst code.

## Code style

The maintainer uses Rider with these rules; match them in every C# file.

- 4 spaces. Braces on their own lines (Allman / BSD) and **always present**, also around single statements of `if`,
  `else`, `for`, `foreach`, `while`, `do`, `using`, `fixed` and `lock`. Stacked `using` / `fixed` headers may share
  one block.
- Explicit access modifiers: `private` on members, `internal` on top-level types that have no other modifier.
- Naming:
  - types, methods, properties: `PascalCase`;
  - private fields: `_camelCase`; private static readonly fields: `PascalCase`;
  - public and internal fields (ECS components, plain data structs): `camelCase`;
  - constants: `UPPER_SNAKE_CASE` (`LockstepProtocol.MAX_PLAYERS`);
  - events: suffix `Event` (`StartedEvent`, `DesyncDetectedEvent`);
  - Unity-serialized fields: `[SerializeField] private T _camelCase`; serializable settings structs use
    `[field: SerializeField] public T Name { get; set; }`.
- **One type per file**, named after the type (partial classes may span `Type.Part.cs` files). Only nested private
  types (and nested types an API pattern requires, like the command buffer `Singleton`s) share a file with their
  parent. ECS components go to the `Components` folders.
- The math API follows the same rules: `FixedPoint`, `FixedVector2`, `FixedVector3`, `FixedQuaternion`,
  `FixedRandom`, `FixedMath` with `Mathf`-like method names (`Sqrt`, `Lerp`, `NormalizeSafe`); vector fields stay
  `x`, `y`, `z`, swizzles are `Xy`/`Xz`.
- `var` for locals; expression bodies only for trivial one-liners.
- C# 9 (Unity): no file-scoped namespaces, no records.
- Comments explain *why*, not what. Public API gets terse XML docs. Code, comments, docs, log messages: English.
- Source files: UTF-8 without BOM.
- Every asset under `Assets` needs a `.meta`: let Unity create it (refresh), never invent GUIDs. Move or rename files
  together with their `.meta`.

## Working with the Unity Editor

The project has `com.unity.pipeline`, which exposes the running editor to the `unity` CLI
(`%LOCALAPPDATA%\Unity\bin\unity.exe`), discovered from the project directory:

```bash
export MSYS_NO_PATHCONV=1                                   # Git Bash mangles /paths in arguments otherwise
unity --no-banner --json command editor_status              # must report "ready"
unity --no-banner --json command menu --path "Assets/Refresh"   # import changes while the editor is unfocused
unity --no-banner --json command recompile                  # then poll recompile_status
unity --no-banner --json command recompile_status           # "completed" / "up_to_date"; errors listed
unity --no-banner --json command console_status             # compile-failure flag, error and warning counts
unity --no-banner --json command console --tail 50 --level error
unity --no-banner --json command --timeout 900 run_tests --mode editor --filter_type assembly --filter Pragma.Lockstep.Tests.Editor
```

`recompile` may find the editor already compiled the change: compare the timestamps of
`Library/ScriptAssemblies/Pragma.Lockstep*.dll`. If a command hangs, `editor_status` reports a blocking modal dialog.

Without a running editor, run the tests in batch mode from a copy of the project (the open editor locks this one):
`Unity.exe -batchmode -nographics -projectPath <copy> -runTests -testPlatform EditMode -assemblyNames
Pragma.Lockstep.Tests.Editor -testResults <xml>`. Copies under long temp paths hit the 260-character limit; map them
to a drive letter with `subst`.

## Tests

- `Assets/Tests/Editor` is the EditMode suite: math accuracy and Burst equality, golden results, serialization,
  checksums, simulation rules, sessions (latency, jitter, late join, disconnects, desync attribution, replays),
  presentation, GameObject views, offline mode and a real Netcode server with two clients.
- The suite reaches internals through `InternalsVisibleTo` in `Runtime/AssemblyInfo.cs`.
- `Assets/Tests/Editor/AssemblyInfo.cs` has `[assembly: DisableAutoCreation]`: test systems enter simulations only
  through `LockstepSimulationOptions.AdditionalSystems`. In `Support`, `SessionHarness` runs a server and clients over
  the loopback network, `FrameBuilder` writes frames by hand in the wire format and `ViewHarness` is a presentation
  world with the view systems and a runtime catalog.
- MonoBehaviours a test adds to a GameObject (view parts) go to `Assets/Tests/Runtime`: `AddComponent` refuses scripts
  from Editor assemblies ("Can't add script behaviour ... because it is an editor script").
- Settings and configs are structs with properties: never mutate a struct-typed property in place
  (`config.StartData.Add(x)` changes a copy and compiles silently); build a local and assign it.
- The Netcode integration test disables `LockstepInputSystemGroup` in its worlds, so the sample's input system does
  not interfere.
- Name tests `Subject_ExpectedBehaviour`. New behaviour gets a test; protocol and simulation changes get a session
  test with latency and jitter.

## Documentation

Keep in sync with code changes: the package README (API, defaults, behaviour), the root README, the CHANGELOG, the
sample README, the skills in `Skills~` (they quote API names, defaults and limits) and `documentation.md`
(design decisions).

## Git

Do not commit or push unless asked: the maintainer reviews and commits changes. The maintainer prefers answers in
Russian; everything in the repository stays in English.
