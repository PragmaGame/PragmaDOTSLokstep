# Pragma DOTS Lockstep: design notes

How the package is built, what it took from ME.BECS and Photon Quantum, what it deliberately left out and why. The
research behind it is in [mebecs_research.md](mebecs_research.md) and [photon_research.md](photon_research.md); the
user documentation is the [package README](Assets/PragmaDOTSLockstep/README.md).

No code was copied from either package. The few algorithms they share with this package (Q16 fixed point, PCG32,
SplitMix64) are public, standard algorithms, implemented here from scratch.

## Contents

- [Goals and constraints](#goals-and-constraints)
- [Architecture in brief](#architecture-in-brief)
- [Comparison](#comparison)
- [Taken from Photon Quantum](#taken-from-photon-quantum)
- [Taken from ME.BECS](#taken-from-mebecs)
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
| Presentation | Views module, interpolation from a world copy | Entity views, four frames, misprediction smoothing | `LockstepViewSystem` / direct reads, `LockstepTransformPrevious` interpolation |
| Transport | Photon, Ragon, local | Photon Realtime | Netcode for Entities RPC, loopback, custom `ILockstepTransport` |

## Taken from Photon Quantum

| Idea | In Quantum | In this package |
|---|---|---|
| The server orders input and never simulates | Server plugin confirms input per tick | `LockstepServer` closes ticks on its own clock |
| Joins, leaves and commands inside the input stream | Player data and commands are RPCs in the confirmed input | Frame records with `Joined`, `Left`, `Commands` flags; players are entities created on the same tick everywhere |
| Fixed-size per-tick input struct plus reliable commands | DSL `input`, `DeterministicCommand` | Any unmanaged struct (`LockstepPlayerInput`), `LockstepCommand` with a stable type hash; commands never repeat |
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
| Views separated from the simulation | Views module spawns from simulation data, frozen during re-simulation | `LockstepViewSystem` mirrors simulation entities; nothing writes back |
| "Who am I" only outside the tick | `GetActivePlayer()` asserts it is not in a tick | The simulation has no notion of a local player; `LocalSlot` exists only on the client object |
| Replays as input logs | Event log and timeline window | Frame log (`LockstepFrameHistory`) and replay export from client, server and the debug window |

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

**Physics, navigation, prediction culling, the Photon server plugin** (Quantum). Out of scope: the request was a
lockstep layer for Entities and Netcode for Entities. Gameplay collisions are written with `FixedPoint` (the sample does).

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

## Limitations and future work

- **Prediction and rollback**: a predicted copy of the simulation world, restored from the confirmed one and
  re-simulated when frames arrive.
- **Snapshot late join**: serialize the confirmed world for joiners of long matches.
- **Deterministic physics** on `FixedPoint`.
- **Input compression** for large input structs.
- **Diff tooling**: a window that compares per-component hashes and entity dumps of two clients side by side.
