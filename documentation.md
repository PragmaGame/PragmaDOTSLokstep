# Pragma DOTS Lockstep: design notes

How the package is built, what it took from ME.BECS, Photon Quantum and ECV, what it deliberately left out and why.
The research behind it is in [mebecs_research.md](mebecs_research.md) and [photon_research.md](photon_research.md); the
user documentation is the [package README](Assets/PragmaDOTSLockstep/README.md).

No code was copied from ME.BECS or Photon Quantum. The few algorithms they share with this package (Q16 fixed point,
PCG32, SplitMix64) are public, standard algorithms, implemented here from scratch. The GameObject views are a port of
ECV, the view layer of the author's own DawnOfWar project, adapted to lockstep.

## Contents

- [Goals and constraints](#goals-and-constraints)
- [Architecture in brief](#architecture-in-brief)
- [Comparison](#comparison)
- [Taken from Photon Quantum](#taken-from-photon-quantum)
- [Taken from ME.BECS](#taken-from-mebecs)
- [Taken from ECV](#taken-from-ecv)
- [Not taken, and why](#not-taken-and-why)
- [Decisions specific to Unity DOTS](#decisions-specific-to-unity-dots)
- [Limitations and future work](#limitations-and-future-work)

## Goals and constraints

- Deterministic lockstep for **Unity Entities 6.6** on top of **Netcode for Entities 6.6**, not a custom ECS: gameplay
  is written as ordinary `ISystem`s, components and bakers.
- Netcode provides what it is good at (connections, handshake, transport, client/server/thin-client worlds); the
  lockstep protocol must not depend on it, so offline play, tests and other transports use the same code.
- Bit-identical results in Mono, IL2CPP and Burst on every platform, without `float` anywhere in the simulation.
- Correctness first: a first version that cannot desync by construction (strict lockstep, exact late join) and tools
  that find the cause when game code does desync.

## Architecture in brief

- **Server** (`LockstepServer`): orders input, never simulates. Closes tick `T` at server time `T + 1`, repeats a late
  player's previous input (optional bounded wait), moves late commands to the next tick, puts joins and leaves into the
  frames, streams frames reliably, measures how early each client's input arrives, compares checksums by majority.
- **Client** (`LockstepClient`): an input clock steered by the server's timing feedback, a playout clock with a
  jitter-aware buffer and a catch-up budget, checksums every N ticks, replays.
- **Simulation** (`LockstepSimulation`): an isolated Entities world, not in the player loop, stepped once per
  confirmed frame; systems join it through `LockstepSimulationSystemGroup`.
- **Transports**: `ILockstepTransport`; a Netcode adapter with one RPC; an in-process loopback for offline play and
  tests.
- **Math**: `FixedPoint` (Q47.16), vectors, quaternions, `FixedMath` with integer-only transcendental functions, `FixedRandom`
  (PCG32).

## Comparison

| Topic | ME.BECS | Photon Quantum | Pragma DOTS Lockstep |
|---|---|---|---|
| ECS | Own | Own | Unity Entities |
| Model | Optimistic event lockstep with rollback | Prediction and rollback on server-confirmed input | Strict lockstep on server-confirmed frames |
| Server | Relays events, provides time | Orders input, fills missing input after a tolerance, compares checksums | Orders input, repeats late input at once (optional wait), compares checksums by majority |
| Input | Discrete events: method id + payload | Per-tick input struct + commands | Per-tick input struct + commands |
| Input timing | `inputLag + ping/2` | Input offset from RTT, time dilation | Input clock steered by measured arrival earliness |
| Math | `sfloat` soft float; `sin` via `System.Math` | `FP` Q48.16 (16 fractional bits) with lookup tables | `FixedPoint` Q47.16 (16 fractional bits), integer series, no tables |
| Random | xorshift32, per-entity seeds | PCG32 in frame globals | PCG32 (`FixedRandom`) as a singleton or per component |
| State hash | Entity counts only, on final snapshots | CRC64 of the serialized frame | xxHash3 of every component field, padding-free, entity-id-free |
| Desync diagnosis | Entity dumps, journal | Frame dumps, frame differ | Majority attribution, per-component hashes, replays to the first bad tick |
| Late join | Not implemented | Snapshot from another client | Re-simulation from tick 0 |
| Replays | Event log, timeline window | Input history + checksums, verification | Frames + checksums, verification |
| Offline | Local transport | Local mode | Offline mode over the same protocol (loopback) |
| Presentation | Views module, interpolation from a world copy | Entity views, four frames, misprediction smoothing | GameObject views ported from ECV, `LockstepViewSystem` Entities Graphics copies, direct reads; `LockstepTransformPrevious` interpolation |
| Transport | Photon, Ragon, local | Photon Realtime | Netcode for Entities RPC, loopback, custom `ILockstepTransport` |

## Taken from Photon Quantum

| Idea | In Quantum | In this package |
|---|---|---|
| The server orders input and never simulates | Server plugin confirms input per tick | `LockstepServer` closes ticks on its own clock |
| Joins, leaves and commands inside the input stream | Player data and commands are RPCs in the confirmed input | Frame records with `Joined`, `Left`, `Commands` flags; players are entities created on the same tick everywhere |
| Fixed-size per-tick input struct plus reliable commands | DSL `input`, `DeterministicCommand` | Any unmanaged struct (`LockstepPlayerInput`), `LockstepCommand` with a stable type hash and data of any length; commands never repeat |
| Button edges from the input history | `Button` keeps frame states | The simulation keeps the previous input: `GetPrevious<T>()`, `HasChanged()` |
| Missing input is replaced, not waited for | `InputHardTolerance`, repeated input, `ReplacedByServer` | The previous input repeats immediately; `MaxInputWaitTicks` makes waiting opt-in |
| Q16 fixed point, rounding multiplication, no implicit float | `FP` with `RawValue`, `(a*b + half) >> 16`, float cast is a compile error | `FixedPoint` with `rawValue`, half-up rounding, conversions from `float`/`double` explicit |
| Deterministic decimal parsing | `FP.FromString` | `FixedPoint.Parse` / `TryParse` |
| PCG32 seeded through SplitMix64, stored in the state | `RNGSession` in the globals | `FixedRandom` in the `LockstepRandom` singleton |
| All state in the frame, stateless systems | Documented rule, analyzer for statics | Rule 3 of the package; components only |
| Checksums every N ticks, compared by the server | `ChecksumInterval` (60), `TickChecksumError` | `ChecksumInterval` (60), majority vote, slot attribution |
| Same configuration for everybody | The server echoes the first client's config | The server sends `LockstepSessionConfig` (tick rate, players, input size, seed, start data) |
| Replays that verify checksums | Replay file with input history and checksums | `LockstepReplay` / `LockstepReplayPlayer.SimulateToEnd()` |
| Local mode on the same code path | `DeterministicNetworkLocal` | `LockstepOfflineSystem` over `LockstepLoopbackNetwork` |
| Interpolation between the last two states | `PredictedPrevious` + `InterpolationFactor` | `LockstepTransformPrevious` + `InterpolationAlpha` |
| Rejecting incompatible builds | `MemoryLayoutVerifier` | `DefaultSimulationHash` (protocol version + simulation systems) checked on join |

## Taken from ME.BECS

| Idea | In ME.BECS | In this package |
|---|---|---|
| Tick derived from a clock, catch-up loop with a time budget | `targetTick = ceil(time / tickTime)`, `maxFrameTime` | Playout clock with `MaxTicksPerUpdate` and `MaxSimulationMillisecondsPerUpdate` |
| Network bookkeeping outside the simulated state | A separate network world as allocator | Protocol state lives in `LockstepServer` / `LockstepClient`; the simulation world holds only game state |
| Small transport interface | `INetworkTransport.Send/Receive` | `ILockstepTransport.Send`; received bytes go to `OnPacket` |
| Input ordered by tick and player | Sorted per-tick package lists | Frames list player records in slot order; commands in queue order |
| Hash only final states | Only snapshots outside the rollback window | Only confirmed ticks are ever simulated, so every hash is final |
| Deterministic ids for new entities | Pre-allocation by job item, sorted batches | `LockstepEntityId` assigned in query order at the end of the tick |
| Per-entity random streams in the state | `Ents.seeds` | `FixedRandom.CreateFromIndex(seed, lockstepEntityId.value)` stored in a component once the id is assigned |
| Views separated from the simulation | Views module spawns from simulation data, frozen during re-simulation | `EntityViewManagerSystem` and `LockstepViewSystem` mirror simulation entities; nothing writes back |
| "Who am I" only outside the tick | `GetActivePlayer()` asserts it is not in a tick | The simulation has no notion of a local player; `LocalSlot` exists only on the client object |
| Replays as input logs | Event log and timeline window | Frame log (`LockstepFrameHistory`) and replay export from client, server and the debug window |

## Taken from ECV

ECV (Entity Component View, `DOTS.ECV` in DawnOfWar, an RTS on Entities 1.4 and Netcode ghosts) shows entities with
GameObjects: a pooled `EntityView` prefab per entity key, one `EntityComponentView<T>` MonoBehaviour per component type,
and one `EntityViewUpdateSystem<T>` per type that pushes the components whose chunks changed. It lives in the same world
as the entities it shows, which lockstep forbids: a presentation write to a simulated entity, even a tag, moves it to
another archetype and changes the chunk order, the state hash and the order `LockstepEntityId`s are handed out in, so
the clients diverge. The port keeps the shape and replaces everything that wrote to the shown world.

| Idea | In ECV | In this package |
|---|---|---|
| Root view plus one part per component type | `EntityView`, `EntityComponentView<T>`, `IEntityComponentView<T>` | Same names and members, except that only the manager binds a root; several parts may show one type; parts of nested views stay with their own root |
| Parts that react to changes only | `EntityComponentViewUnmanaged<T>` with `EqualityComparer<T>` | Same, compared bytewise (the default comparer of a struct without `IEquatable` reflects and boxes) |
| Cache reset when a pooled view is reused | `Bind` clears the last value | Same |
| One generic system per component type | `EntityViewUpdateSystem<T>` with a change filter and `ConfigureQuery` | Same API; chunks of the simulation world filtered by its change versions, plus every value for views spawned, attached or forced since the last push |
| Views from a pool | `com.pragma.pool`, `EntityView : PrefabPoolObject` | `IEntityViewPool`, set per project (`EntityViewManagerSystem.PoolFactory`) or per world; `EntityViewPool` by default; com.pragma.pool through a small adapter |
| One place that spawns and returns views, returns first | `EntityViewManagerSystem` | Same |
| Catalog of view prefabs apart from the model prefabs, linked by a key | `EntityViewConfig`, `EntityPrefabViewRegistry`, `EntityIdentifier` | `EntityViewConfig` and `EntityViewKey`: baked (`EntityViewConfigAuthoring`) or registered at runtime (`EntityViewConfigProvider`) |
| No view graphics in server builds | The baker skips `NetcodeConversionTarget.Server` | Same |
| Index from entity to view | `EntityViewManager` | Same, plus `Attach` for views the caller owns (what `SingletonEntityViews` was for) and `ForceUpdate` |
| Transform part | `TransformComponentView` fed with `LocalTransform` | Same part, fed with `LockstepTransform` interpolated between ticks |

**Adapted, and why.**

- **Lifetimes by comparison.** ECV tags entities with a cleanup component and returns a view when only the cleanup
  "shell" is left. A presentation may not add anything to a simulated entity, so the manager compares the keyed
  entities with its views, and only when a tick created, destroyed or re-keyed some (the order version and the change
  versions of `EntityViewKey`), or a view was destroyed from outside.
- **Change detection across worlds.** A presentation system cannot use the change filters of a query in another world
  (they are tied to the system that owns the query), so the update systems compare chunk change versions with one
  below the simulation version they saw last: every system bumps the global version once more when it finishes, so a
  write made outside systems after a push (the session's `LockstepTime`) carries exactly the version seen. Toggling an
  enableable component of the query counts as a change too, because it moves entities in or out of the query. Views
  spawned since the last push get every value, which replaces ECV's `forceUpdateVersion` write.
- **Pool state.** A pooled view starts every binding fresh: the cached values of its parts are forgotten and
  auto-update comes back on, so a previous owner's pause or value never leaks to the next entity.
- **Binding belongs to the manager.** ECV binds in the pool's spawn and release callbacks, so the root's `Bind` and
  `BindBreak` are public. Here the manager binds right after `Spawn` and unbinds right before `Release`, whatever the
  pool, and the root's methods are private: a part told by anyone else that its entity is gone would restore its pooled
  state while still on screen.
- **A key in the simulation.** ECV keys views by `EntityIdentifier`, part of another framework. `EntityViewKey` is
  ordinary simulation data, so entities created in code (the sample) get views as well as baked prefabs, and changing
  the key swaps the view.
- **No managed components.** They are deprecated in Entities 6.6: the catalog is an unmanaged buffer with
  `UnityObjectRef<GameObject>`, and the index lives in the system, found with `EntityViewManager.TryGet(world)`.
- **Runtime catalogs.** Lockstep games create their worlds on demand (a lobby, a match), after the subscenes have
  loaded into the worlds that existed, so a catalog can also be registered at runtime.
- **Lazy initialization.** Views collect their parts on first use, not only in `Awake`, which does not run for
  instances created in edit mode (the tests) or spawned inactive.

**Not taken from ECV.**

- `ViewReferenceComponent` (a managed reference from the entity to its view) and `AutoUpdateViewTagComponent`: both
  live on the shown entity. `EntityViewManager.TryGetView` and `EntityView.IsAutoUpdateEnabled` replace them.
- `IEntityView`: nothing consumed it in ECV either; the manager and the pools work with `EntityView`.
- `ConfigStorageComponent` and `EntityConfig`: configuration storage built on managed components and DOTS.Common
  commands, not part of the views.
- The editor preview of authored data on view prefabs (`IEntityAuthoringRefreshHandler`): it belongs to the
  EntityCompositeAuthoring framework. The boxed `EntityView.UpdateData(IComponentData)` stays for such tools.
- Odin's key dropdown and the `WorldResolver` service lookup: dependencies the package does not take; keys are plain
  strings and the pool comes from `PoolFactory`.
- The game's views (health bar, player color, selection). Selection in particular is local to a player and belongs to
  the presentation in lockstep, not to a simulation component as in DawnOfWar.

## Not taken, and why

**Prediction and rollback** (both). Rollback needs a snapshot of the state every tick and fast restores. Quantum and
ME.BECS can copy a whole state with a few `memcpy` calls because their memory uses offsets instead of pointers. An
Entities world cannot: chunks hold pointers, entity ids come from a process-wide store, and copying worlds through
the `EntityManager` is far too slow to do every tick. Strict lockstep is correct by construction and enough for the
genres this package targets. The isolated world, the confirmed frames and the state hash are the parts a predicted
world can later be built on.

**Soft float** (ME.BECS `sfloat`). Emulated IEEE floats are much slower than fixed point, and ME.BECS's `sin` still
calls `System.Math`, so its trigonometry is platform dependent anyway. Fixed point is integer arithmetic: fast in
Burst and identical everywhere.

**Two math modes behind a define** (ME.BECS `tfloat`). Compiling the same code with `float` or `sfloat` invites float
leaks through implicit conversions. There is one `FixedPoint` type, and conversions from `float` are explicit.

**Lookup tables for trigonometry** (Quantum). Megabytes of tables that must be loaded before the simulation runs. The
package computes `sin`, `atan`, `exp`, `log` with integer series in Q30 after range reduction: within 1–3 steps of
`double` (`exp` and `tan` within 0.02 %), no assets, Burst-friendly.

**Snapshot late join** (Quantum). A snapshot must contain every bit of state; Quantum's changelog lists many late-join
desyncs from state missing in it. With Entities it would also need world serialization that survives different entity
ids, blob assets and baked content. Re-simulating from tick 0 is exact by construction; its cost grows with the match
length, which is why snapshots stay on the roadmap.

**Unreliable input with redundancy and delta compression** (Quantum). Netcode RPCs are reliable and ordered, so
redundancy is unnecessary. Frames already carry only players whose input changed, and clients send a one-byte repeat
flag for unchanged input.

**Time dilation** (Quantum). It exists to keep prediction within the rollback window. In strict lockstep a slow client
only delays its own input, which the server replaces by the previous one.

**Events with confirmation and cancellation** (Quantum). Without prediction every simulated tick is final, so there is
nothing to cancel. Presentation reads state (or a simulation-owned event buffer).

**DSL and code generation** (Quantum). Entities already has components, systems, baking and source generators;
gameplay stays plain C#.

**Frame heap collections** (Quantum). Dynamic buffers and components cover the same needs inside Entities.

**Network methods as function pointers** (ME.BECS). Method ids from name-sorted registration can differ between
builds. Commands are typed structs identified by a stable hash of their assembly-qualified type name, the same in
every build of the same code.

**IL scanning for parallel entity creation** (ME.BECS). Entities already defers structural changes through command
buffers; sort keys and `LockstepEntityId` make the result deterministic.

**Shallow state hash** (ME.BECS). Hashing only counts misses almost every real desync. The checksum here covers every
component field.

**`max(server time, local time + dt)` clock** (ME.BECS). It never slows a fast client down and drifts with truncated
milliseconds. The input clock here is steered by how early the server actually receives each client's input, which
also accounts for server load and asymmetric routes.

**Physics, prediction culling, the Photon server plugin** (Quantum). Out of scope: the request was a lockstep layer for
Entities and Netcode for Entities. Gameplay collisions are written with `FixedPoint` (the sample does). Navigation
came later, for DawnOfWar, as a grid of its own rather than Quantum's navmesh (decision 11).

## Decisions specific to Unity DOTS

These came up while building on Entities; neither reference package deals with them.

1. **Entity ids are process-global.** Entities 6.x allocates entity index and version from one store shared by every
   world of the process (`EntityComponentStore.s_entityStore`). Two identically built worlds therefore have different
   `Entity` values, and so do two clients. Found when identical worlds hashed differently. Consequences: the checksum
   identifies entities by traversal position and replaces `Entity` fields with the position of their target;
   `LockstepEntityId` provides identities that match across clients; "never use `Entity` as data" is a documented
   rule. Query and chunk order are deterministic, because they depend only on the world's own structural changes.
2. **Isolated world through a custom filter flag.** `LockstepSimulationSystemGroup` carries
   `WorldSystemFilterFlags` bit 28, which its members inherit, so gameplay systems are never created in client,
   server or default worlds. Creation order is sorted (full name, then `TypeManager.SortSystemTypesInCreationOrder`).
   A step writes the frame into a buffer, updates `LockstepTime` and the world time, resets the update allocator,
   updates the world and completes all jobs.
3. **Checksum over field bytes.** Field ranges come from reflection, so padding, pointers and blob references are
   skipped; component types are ordered by stable type hash because type indices differ between builds; enableable
   bits are masked; a per-type breakdown points at the diverging component.
4. **Burst pitfalls.** Managed arrays in a Burst `OnCreate` (for example `params ComponentType[]`) fail to compile
   with Burst, which then silently falls back to managed code: archetypes are created with
   `stackalloc ComponentType[] { ... }`, and the console has to be checked for "Burst error".
5. **No managed components.** They are deprecated in Entities 6.6, so running sessions are found through the static
   `LockstepWorlds` registry keyed by `World.SequenceNumber`.
6. **Netcode for Entities as a transport only.** One manually serialized RPC carries every packet (up to 1024 bytes;
   a framer fragments larger messages). Ghosts and prediction are not used. Connections are tracked by entity, since a
   `NetworkId` can be reused; thin clients join without simulating.
7. **Fairness for late input.** The previous input repeats immediately, so one slow player does not slow anybody
   down; commands are never dropped; waiting is opt-in (`MaxInputWaitTicks`).
8. **Desync attribution.** The server compares checksums by majority and names the clients that disagree, so the
   wrong machine can be told apart from the right ones (with two players, both are flagged).
9. **Presentation that never writes.** Entities ties the usual presentation tools (cleanup components, query change
   filters) to the world being shown. The views read a world they may not touch, so they find lifetimes by comparing
   entities with views and changes by comparing chunk change versions with the last version they saw. A test runs two
   clients, one of them with views, and checks that their state hashes stay equal.

10. **Scene content.** A map's buildings and markers live in a subscene of the presentation world, which the
    simulation must never read. Like the prefab registry, they are copied into the simulation world before tick 0
    (`LockstepSceneEntity`), the counterpart of Netcode's pre-spawned ghosts. The copy is sorted by an order baked
    from each object's `GlobalObjectId`, because the query order of the presentation world follows the order its
    subscene sections loaded in, which may differ between clients. The archetypes of the copies are created in that
    order before the copy: `CopyEntitiesFrom` moves chunks in an order that depends on where the source allocated them,
    and the order of archetypes is state that queries and the checksum walk. The prefab registry is copied the same
    way.

11. **Navigation on a grid.** RTS units need paths around buildings, and NavMesh is float-based and built per
    platform. `Pragma.Lockstep.Navigation` keeps a walkability grid in the simulation world as plain state: a buffer
    of obstacle counts, so obstacles stamp and release cells in any order with the same result, and the checksum
    covers it. Obstacles are entities; the rectangle each one stamped is kept in a cleanup component, which is how a
    destroyed building releases its cells. The search is A* with integer costs and a heap with a strict total order;
    line of sight for string pulling compares cross products of raw fixed-point values, so it is exact, and a segment
    through a cell corner counts both cells beside it. Paths are planned in a parallel job: each depends only on the
    grid and its own agent, so the thread count cannot change a result. Walking agents re-check their remaining
    segments when the grid version changes and plan again only when they are blocked. A grid was chosen over a
    fixed-point navmesh because it is simple to keep exact, cheap to change at run time (buildings) and easy to hash;
    a navmesh with a funnel algorithm would give smoother paths on large open maps. Agents keep apart by separation,
    not by avoidance: after they walked, overlapping agents are pushed apart by half of the overlap per tick, a walking
    agent taking three quarters of it from a standing one so that units holding a place are not swept along. Each
    push is a function of the pair alone and the pushes of an agent are summed in integer math, so the result does
    not depend on the visiting order; positions are read from a copy taken before any agent moves. Predictive
    avoidance (RVO) would steer agents around each other before they touch, at the cost of velocity state and an
    iteration whose result depends on its order. The ground is part of the grid: fixed-point heights at the cell
    corners, baked from a terrain, so a unit's Y is simulation state every client computes alike (bilinear inside a
    cell) instead of something each renderer guesses. Agents walk on the XZ plane and take the ground's height after
    every step and push; slopes steeper than a limit block their cells for good when the cells are built, the way the
    gradient-based impassability map of classic RTS editors does, so pathfinding and placement need no extra rule.

12. **Command data next to the commands.** A buffer element has a fixed size, so the payload struct stays inline
    (up to 122 bytes) and lists of any length (the unit ids of an order) go to a second buffer, `LockstepCommandData`,
    on the same entity; a command keeps the offset and length of its data. The network never limited this: the
    framer fragments any message, and Netcode queues what its reliable window cannot take yet. Fixed lists capped by
    the payload size made games split one order into several commands and stitch them back together; with data, an
    order is one command. On the wire a command is type hash, payload, data length and data, and the client and the
    server keep commands in that form, so the server checks and relays bytes without decoding them. Data starts on
    8-byte boundaries in the buffer, with zeroed padding, so any element type reads aligned and the checksum sees the
    same bytes everywhere.

13. **Stats as buffers with change versions.** Upgrades, research, abilities and auras all change numbers of units,
    and patching component fields by hand loses track of who changed what and leaves no way to end an effect. Each
    entity keeps its stats and their modifiers in buffers; the game names its stats with its own ids (an enum, which
    the helpers convert), so the package knows no stat list and an entity carries only the stats it has. Modifiers are
    removed by their source (an effect that ends or is applied again takes all of its modifiers along) or by an end
    tick, which keeps timed effects in the simulation state instead of in timers. The stat system recalculates only the
    chunks where something changed, by change versions, and writes stats only then, so systems that apply a stat to
    another component wake only when it changed. Versions are allowed to decide this because a recalculation is
    idempotent: a chunk recalculated without need gets the same bytes, so the state never depends on how versions
    advance on a machine (a late joiner replaying many ticks per frame included). Further choices, made for strategy
    games (Dawn of War style research, squads, auras):
    - **Attributes and resources.** Health is not a component next to the stats but a resource stat capped by the max
      health attribute: health, morale, energy and money share one pool rule (never below zero, capped, fitted when the
      cap changes by keeping the share or the amount) and one way to change them, one-tick `LockstepStatChange`s summed
      per tick, so the result never depends on the order attackers were processed in. Modifiers apply to attributes
      only, which keeps "a buff to current health" meaningless rather than surprising.
    - **Grants instead of copies.** A research applies to all units of a type, including those built later, and a
      squad ability to every member, reinforcements included. Copying modifiers into each unit would make every spawn
      path know every effect; instead the player or squad entity holds `LockstepStatGrant`s and receivers name it in
      `LockstepStatGrantor`, so the stat system reads the grants while recalculating and a receiver created later gets
      them in place. Receivers of a grantor recalculate when its chunk's grants changed. The link is an `Entity`
      reference, not a `LockstepEntityId`, because ids are assigned at the end of the creation tick and a squad and its
      members are created together; the checksum hashes `Entity` fields by their target, and the reference is never
      sorted, hashed or sent.
    - **Non-stacking by source kind.** Two leaders with the same aura must not double it, and choosing the strongest at
      the applier would need every aura system to track its rivals. A `Strongest` modifier competes with the others of
      its stat, type and source kind, own and granted alike, and the strongest one applies.
    - **Percentages stop at zero.** A factor below zero flips the sign of an attribute: a 60 % and a 50 % slow summed as
      additive shares would make a speed negative, and two multiplicative -150 % debuffs would cancel out into a quarter
      of the speed. Both percentage factors are floored at zero, so any pile of debuffs ends at zero. The flat part and
      the result are not floored: negative armor or regeneration are legitimate values, and a game that needs other
      bounds clamps where it reads the stat.

## Limitations and future work

- **Prediction and rollback**: a predicted copy of the simulation world, restored from the confirmed one and
  re-simulated when frames arrive.
- **Snapshot late join**: serialize the confirmed world for joiners of long matches.
- **Deterministic physics** on `FixedPoint`.
- **Navigation**: predictive avoidance between agents (they only separate once they overlap), flow fields for large
  groups, cell costs, paths for several agent sizes.
- **Input compression** for large input structs.
- **Diff tooling**: a window that compares per-component hashes and entity dumps of two clients side by side.
