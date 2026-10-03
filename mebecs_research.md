# ME.BECS: determinism and networking

Research notes on how ME.BECS implements a deterministic, networked ECS, made while designing Pragma DOTS Lockstep.
Source: the embedded copy of `com.me.becs` 2025.11.27-rc1 in `MEBECS_sample/Assets/ME.BECS-main` (paths below are
relative to that folder). What was taken from it and why is in [documentation.md](documentation.md).

## Contents

- [Overview](#overview)
- [Network module](#network-module)
- [Transport abstraction](#transport-abstraction)
- [Determinism techniques](#determinism-techniques)
- [Hashing and desync detection](#hashing-and-desync-detection)
- [Serialization, snapshots and replays](#serialization-snapshots-and-replays)
- [Players and commands](#players-and-commands)
- [Views and presentation](#views-and-presentation)
- [Tests](#tests)
- [Strengths](#strengths)
- [Weaknesses and pitfalls](#weaknesses-and-pitfalls)

## Overview

ME.BECS is a custom ECS (not Unity Entities) with its own memory allocator, Burst-compiled systems and add-ons for
networking, views, pathfinding, fog of war and more. Its networking is **optimistic lockstep with rollback**, not
stall lockstep:

- Every client simulates up to `targetTick = ceil(serverTimeMs / tickTime)` using a clock supplied by the transport,
  and never waits for other players.
- Input is a discrete *network event*: a method id plus a payload, stamped for a future tick. The server relays every
  event to everybody, including the sender.
- The only prediction is "no event, so nothing happens". When an event arrives for a tick that was already simulated,
  the client restores a snapshot and re-simulates within the same frame.
- Desyncs are detected by comparing hashes of states that are older than the rollback window.

Key files:

| File | Contents |
|---|---|
| `Addons/Network/Runtime/Module/UnsafeNetworkModule.cs` | Packages, event/state/hash storages, tick loop, rollback |
| `Addons/Network/Runtime/Module/NetworkModule.cs` | Settings and defaults, clock update, patch API |
| `Addons/Network/Runtime/Module/NetworkTransport.cs` | Transport interfaces |
| `Addons/Network/Runtime/Module/SortedNetworkPackageList.cs` | Per-tick sorted event list |
| `Addons/Network/Runtime/NetworkWorldInitializer.cs` | MonoBehaviour driving the network world |
| `Addons/Network/Runtime/Transport/` | Local, Dummy, Photon and Ragon transports |
| `Runtime/Core/World/World.State.cs`, `World.Serialization.cs` | World state, hash, serialization |
| `Runtime/FixedPoint/sfloat/` | Soft float and libm |
| `Addons/Views/Runtime/` | Views module and interpolation |

## Network module

`NetworkModule` (a ScriptableObject) wraps the struct `UnsafeNetworkModule`. Its data holds the clock
(`currentTimestamp`, `previousTimestamp`), `localPlayerId`, `tickTime`, `inputLag`, a separate *network world* used
only as an allocator for bookkeeping (so the bookkeeping is never rolled back), the simulation world, and four
storages: events, states (snapshots), methods and hashes.

**Package format** (18-byte header):

```csharp
struct NetworkPackage { byte packageType; ulong tick; uint playerId; ushort methodId; ushort dataSize; byte localOrder; byte* data; }
// Sort order and identity: tick, then playerId, then localOrder.
```

**Defining and sending input.** Users declare static methods with `[NetworkMethod]` (derived from
`MonoPInvokeCallbackAttribute`) taking `(in InputData, ref SystemContext)`. An editor code generator registers them
ordered by method name; the index plus one is the method id. Gameplay code calls
`world.SendNetworkEvent(data, method)` from outside the tick. The method runs on the main thread, as managed code,
through `Marshal.GetDelegateForFunctionPointer`.

**Send path.** The event is serialized, stamped with `targetTick - negativeDelta + inputLag + transport.InputLagInTicks`
and a per-player byte counter `localOrder`, and handed to the transport. Every shipped transport sends to the network
only, so local input also comes back through the server echo and local and remote input share one code path.
`negativeDeltaTicks` lets a caller backdate an event, forcing a rollback on every client.

**Receive and buffering.** `EventsStorage` keeps a dictionary from tick to a list sorted by binary insertion. An event
with an existing key (tick, playerId, localOrder) is rejected, so duplicates are harmless and arrival order does not
matter. Events are never pruned: the whole match history stays for replays.

**Clock and tick loop.** `NetworkWorldInitializer.Update` (execution order -10000) advances the clock:

```csharp
if (transport.ServerTime > GetCurrentTime() && networkState == Normal) SetServerTime(transport.ServerTime);
else SetServerTime(GetCurrentTime() + dtMs);              // dtMs = (uint)(Time.deltaTime * 1000)
GetTargetTick() => (ulong)math.ceil(currentTimestamp / tickTime);
```

`Update` then completes jobs, receives hashes, drains received packages, rolls back if needed, copies the whole world
into `startFrameState` (for view interpolation) when ticks are pending, and runs the tick loop: for each tick it takes
a snapshot every `copyPerTick` ticks, invokes the tick's events in sorted order, updates the world, sends a hash, and
stops early when a rollback is required or the frame time budget (`maxFrameTime`) is exceeded.

**Rollback.** Triggered at the start of `Update` when an event arrived for an already simulated tick. Snapshots live
in a ring (`capacity` entries, one every `copyPerTick` ticks). The newest snapshot at or before the late tick is
restored (`state.CopyFrom`), newer ones are invalidated, and the normal loop re-simulates up to the target tick within
the frame budget, re-snapshotting on the way. Without a suitable snapshot it falls back to a lazily cloned reset state
("you are run out of state's history"). Views are frozen while re-simulating.

**Defaults:**

| Setting | Code default | Sample asset |
|---|---|---|
| `tickTime` | 33 ms | 33 ms |
| `maxFrameTime` | 100 ms | 10 ms |
| `inputLag` | 1 tick | 1 tick |
| Snapshot `capacity` | 10 | 20 |
| `copyPerTick` | 30 | 10 |
| Rollback window | 300 ticks (~9.9 s) | 200 ticks (~6.6 s) |

## Transport abstraction

```csharp
public interface INetworkTransport {
    void OnAwake(); void Dispose();
    JobHandle Connect(in World world, NetworkModule module, JobHandle dependsOn);
    TransportStatus Status { get; set; }          // Unknown, Connecting, Connected, Disconnected
    EventsBehaviour EventsBehaviour { get; }      // SendToNetworkOnly | RunLocalOnly | StoreLocalAndSendToNetwork
    ulong InputLagInTicks { get; }  double ServerTime { get; }   // milliseconds
    void Send(byte[] bytes);  byte[] Receive();   // Receive is polled until null
}
// Optional: INetworkTransportHashSync, INetworkTransportPreUpdate, INetworkTransportPing, INetworkTransportPackageCallback
```

The transport assigns the local player id, sets the server start time, provides a monotonic `ServerTime` and must
deliver reliably to all clients. Implementations:

- **LocalTransport** (default): loopback queues; can record a replay to `Assets/ME.BECS.Replays/replay_<time>.bytes` or
  play one back. Its latency simulation adds absolute time on every call, so it is effectively broken.
- **DummyTransport**: connects instantly, sends and receives nothing.
- **PhotonTransport** (PUN): inputs as reliable event code 1 to all, hashes as unreliable code 2 to others; the player
  id is the actor number; `InputLagInTicks = (ping / 2) / tickTime + 1` with a ping median over 64 samples.
- **RagonTransport**: `Room.ReplicateData`; the player id is the peer id.

## Determinism techniques

**Soft float.** A `FIXED_POINT` define (set in every build profile) switches an alias block in about 150 files:

```csharp
#if FIXED_POINT
using tfloat = sfloat; using ME.BECS.FixedPoint; using Bounds = ME.BECS.FixedPoint.AABB;
#else
using tfloat = System.Single; using Unity.Mathematics; using Bounds = UnityEngine.Bounds;
#endif
```

- `ME.BECS.FixedPoint` is a generated `sfloat` clone of `Unity.Mathematics` (vectors, matrices, quaternion, `math`,
  `Random`, AABB), so the same source compiles in both modes.
- `sfloat` (from the CodesInChaos SoftFloat project) stores IEEE binary32 bits in a `uint` and implements arithmetic
  with integer mantissa/exponent operations, including subnormals, NaN and infinities. The implicit conversion from
  `float` reinterprets bits, so float literals are exact.
- `libm` (a port of Rust libm / musl) provides integer-only `sqrtf`, rounding, `exp`, `log`, `pow`, `atan`, `atan2`,
  `asin`, `acos`. **Exception:** `sinf` is `(float)System.Math.Sin((double)x)`
  (`Runtime/FixedPoint/sfloat/libm/Trigonometry.cs`), and `cosf`/`tanf` are built on it, so all trigonometry depends
  on the platform.
- `deltaTime` is derived from integer milliseconds.
- Integer "unit" types (`meter` in mm, `usec`, `uangle` in 1/100 degree, ...) with saturating addition also exist;
  they expose `float` operators too, which are not deterministic.

**Random.** A xorshift32 port of `Unity.Mathematics.Random`. The main API is per entity: seeds live in the world
state (`Ents.seeds`) and roll back with it; `ent.GetRandomValue()` atomically increments the entity's seed and
discards four values. The random API asserts that it is called inside a tick.

**Entity ids and ordering.** Ids come from per-type pages with a free bitmask; destroys are deferred and sorted before
ids return to the pool; per-thread structural-change batches are concatenated and sorted by entity id before they are
applied. For `Ent.New` inside parallel jobs, an IL scan of the job's `Execute` counts the creations per item, the
entities are pre-created serially, and item `i` takes a fixed slice, so ids depend on the item index, not the thread.

**Burst.** System wrappers use `[BurstCompile(FloatPrecision.High, FloatMode.Deterministic)]` (which is not a
cross-platform guarantee; `sfloat` is). Every tick is completed. Order-dependent parallel writes are not prevented
(for example a compare-and-swap add on an `sfloat` from a parallel job).

## Hashing and desync detection

```csharp
public int Hash => Utils.Hash(this.entities.Hash, this.components.Hash, this.random.Hash, this.tick);
// entities.Hash = Hash(FreeCount, EntitiesCount, nextGroupId); components.Hash = items.Length
```

- **No component data is hashed**: only entity counts, allocator state, the random state and the tick. Value desyncs
  (positions, health) go unnoticed.
- Only the snapshot evicted from the ring (the oldest, outside the rollback window, therefore final) is hashed; the
  hash is sent once per `copyPerTick` ticks and arrives far behind real time.
- `HashTableStorage` compares every incoming hash with the ones present for that tick and calls
  `OnHashDesync(tick, flags, hashes)` on a mismatch. There is no recovery or resync.
- Debug aids: the `LOGS_NETWORK_SYNC_LOG` define dumps every entity's `ToString()` per snapshot for diffing; the
  `JOURNAL` define records per-thread component and system events.

## Serialization, snapshots and replays

- The whole world is the state headers plus one memory allocator whose internal pointers are `(zoneId, offset)`
  pairs, so cloning is a struct copy plus a memcpy per zone, with no pointer fixups. This is what makes ME.BECS's
  snapshots and rollback cheap.
- `world.Serialize()` writes the raw allocator and headers; it needs the identical build and has no versioning.
- `Patch`: a SIMD diff of two serialized states. Patch and ping package types are declared but unused: **late join by
  state transfer is not implemented**.
- A replay is the event log (`SerializeAllEvents`) plus a reproducible initial state. `ReplaysEditorWindow` shows a
  timeline, scrubs with `RewindTo`, removes single events and saves `.rep` files.

## Players and commands

- The player id is the transport's id (Photon actor number, Ragon peer id). `PlayersSystem` creates a fixed number of
  player and team entities (default 4), and network methods map `data.PlayerId` straight to a player index, so
  transport ids must equal player indices.
- The local "active player" lives outside the simulation in a `SharedStatic`, and reading it asserts that the code is
  *not* inside a tick: simulation code cannot branch on "who am I".
- `ME.BECS.Commands` is not networking: it models RTS orders as components (move, attack, build); a network method
  would set them.
- There is no per-player, per-tick input struct and no "all inputs for tick T received" confirmation: events for tick T
  run on the main thread before the tick's systems and write components that Burst systems read.

## Views and presentation

- The simulation only writes data (`ent.InstantiateView(ViewSource)` sets a `ViewComponent`). Each rendered frame the
  views module diffs entities against what is on screen and spawns (at most 5 per frame), removes or reassigns views.
  Providers: GameObject, DrawMesh, Particles. View data lives in a separate visual world.
- The views module does not run during re-simulation, so rollbacks never churn views.
- Interpolation: between `startFrameState` (the world copied before the frame's ticks) and the current state,
  `factor = unlerp(prevTick * tickTime, curTick * tickTime, now)`; positions and scales are lerped and rotations
  slerped. New entities and entities whose parent changed are not interpolated.
- Simulation-to-UI events go through a visual world and are dispatched once per rendered frame, outside ticks.

## Tests

The network add-on has two tests (sorting of `SortedNetworkPackageList`). Core tests cover serialization round trips,
cloning and patching. Rollback equivalence, out-of-order or duplicate delivery, hash sync, transports, the clock and
cross-platform math are not tested.

## Strengths

1. Tick derived from a clock, a catch-up loop with a per-frame time budget, visuals frozen during re-simulation.
2. Input keyed by (tick, player, order) in sorted per-tick lists; duplicates rejected; the sender consumes its own
   input through the server echo, so local and remote input share one path.
3. Network bookkeeping kept outside the state that is rolled back.
4. Deterministic ids under parallel creation (pre-allocation by job item), deferred and sorted structural changes,
   per-entity random seeds stored in the state.
5. Debug-only guards: random only inside a tick, the local player only outside it.
6. Tooling: input-log replays with a scrubbing timeline, per-tick entity dumps, a journal.

## Weaknesses and pitfalls

1. The hash ignores component values, so most real desyncs are never detected.
2. `sinf` calls `System.Math.Sin`: trigonometry is platform-dependent despite the soft float.
3. Floats can leak in through implicit `float` to `sfloat` conversions, the `Unity.Mathematics` conversions and the
   `float` operators of the unit types.
4. Order-dependent parallel accumulation (compare-and-swap adds, appends under a lock) is allowed.
5. The clock takes `max(server, local + dt)`, so a fast client is never slowed down; `dt` is truncated to whole
   milliseconds; there is no drift correction. `PhotonNetwork.Time` (seconds) appears to be used where milliseconds
   are expected (not verified at runtime).
6. `localOrder` is a byte that is not rolled back and wraps after 256 events per player.
7. Events are never pruned; an event older than the snapshot history re-simulates from the reset state. No late join.
8. Network method ids depend on registration order sorted by short method name: builds can disagree.
9. Snapshots are raw memory: identical builds only; the full-world copy into `startFrameState` happens every frame
   that runs a tick.
