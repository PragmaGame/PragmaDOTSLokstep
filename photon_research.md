# Photon Quantum: determinism and prediction/rollback

Research notes on how Photon Quantum implements its deterministic simulation, made while designing Pragma DOTS
Lockstep. Source: Quantum 3.0.13 Stable (build 2170) in `PHOTON_sample/Assets/Photon/Quantum` (paths below are relative
to that folder). The simulation layer ships as C# source (`Simulation/QuantumSimulationCore.cs`, about 10 000
lines); `Quantum.Deterministic.dll` and `Quantum.Engine.dll` were decompiled with ILSpy for reading only. The server
plugin is not part of the sample, so server behaviour is inferred from configuration docs and protocol messages. What
was taken from Quantum and why is in [documentation.md](documentation.md).

## Contents

- [Overview](#overview)
- [Session and timeline](#session-and-timeline)
- [Input and commands](#input-and-commands)
- [Frames, snapshots and checksums](#frames-snapshots-and-checksums)
- [Determinism techniques](#determinism-techniques)
- [Systems, signals and events](#systems-signals-and-events)
- [Presentation](#presentation)
- [Assets and configuration](#assets-and-configuration)
- [Local mode, replays and saves](#local-mode-replays-and-saves)
- [Strengths](#strengths)
- [Weaknesses and pitfalls](#weaknesses-and-pitfalls)

## Overview

Quantum is a complete deterministic engine with its own ECS (not Unity Entities), DSL code generation (`.qtn`
files), fixed-point math, physics and navigation. Networking is **predict/rollback on top of server-confirmed
input**:

- A Photon server plugin orders input. It does not simulate; it confirms the input of every player per tick, fills in
  missing input after a tolerance, and relays commands, joins and checksums.
- Every client keeps a *verified* frame (simulated with confirmed input only) and a *predicted* frame (verified plus
  prediction up to the current time). Whenever new confirmed input arrives, the predicted frame is thrown away, copied
  from the verified one and re-simulated.

Layers: `SessionRunner` (Simulation source) owns a `DeterministicSession` (DLL, public API), which owns an internal
`DeterministicSimulator` (clock, input timeline, rollback), which calls into `QuantumGame` (input serialization,
systems, events, callbacks).

## Session and timeline

**`DeterministicSessionConfig` defaults** (`Photon.Deterministic.DeterministicSessionConfig`):

| Field | Default | Meaning |
|---|---|---|
| `UpdateFPS` | 60 | Ticks per second |
| `RollbackWindow` | 60 | Maximum ticks predicted ahead of the verified frame |
| `ChecksumInterval` | 60 | Ticks between checksums |
| `InputDelayMin` / `InputDelayMax` | 0 / 60 | Input offset range, in ticks |
| `InputDelayPingStart` | 100 ms | Ping above which the input offset grows |
| `InputHardTolerance` | 8 | Ticks the server waits before replacing missing input |
| `InputRedundancy` | 3 | Times each input is resent (unreliable channel) |
| `InputRepeatMaxDistance` | 10 | How far back remote input prediction may look |
| `InputDeltaCompression` | true | Delta-compressed input |
| `LockstepSimulation` | false | Legacy strict lockstep mode |
| `TimeCorrectionRate` | 4 per second | Clock correction sampling |
| `TimeScaleMin`, `TimeScalePingMin/Max` | 100 %, 100 / 300 ms | Server-driven time dilation (off at 100 %) |

Every client sends its config when starting; the server keeps the first one and echoes it (with the runtime config
bytes) to everybody, so all clients run identical settings.

**One `Session.Update(dt)`:** poll the network and insert confirmed input as verified; send RTT samples (every 0.25 s)
and recompute the input offset; accumulate scaled time and adjust the clock; for every new tick poll local input for
`tick + LocalInputOffset`, insert it as predicted and queue it for sending; then `Simulate()` and run view callbacks
and events.

**Input timeline.** A pooled, sorted linked list of input frames (`Verified` and `Predicted` player masks, 128 players
at most) starting at the last verified tick. A tick is verified only when every player slot has verified input; the
server marks absent players `PlayerNotPresent`.

**Rollback core** (`DeterministicSimulator.Simulate`):

```csharp
bool advanced = SimulateVerified(maxTicks);                  // next tick fully verified and due
if (_session.IsLockstep) return;
if (_statePredicted.Number - _stateVerified.Number >= RollbackWindow) return;
if (advanced) _statePredicted.CopyFrom(_stateVerified);       // rollback = full state copy
while (num < _inputFrameCounter && num + 1 - verified.Number < RollbackWindow)
    Simulate(GetInputFrame(++num), _statePredicted, predicted: true);
```

There is no "the prediction was right, skip the re-simulation" shortcut: every verified advance re-simulates all
predicted ticks, which keeps the worst case predictable. Simulation starts at tick `RollbackWindow - 1` so tick
arithmetic never goes negative.

**Remote input prediction.** A remote player without verified input for a predicted tick gets the latest verified
input flagged `Repeatable` within `InputRepeatMaxDistance` ticks, or zeros.

**Stalling.** When the verified frame falls more than `RollbackWindow` behind, the session stalls: it advances only
ticks that will verify and freezes the interpolation factor.

**Input delay.**
`InputDelayMin + ceil(max(0, (RTT - InputDelayPingStart) * 0.5) / tickDuration * timeScale)`, clamped to
`InputDelayMax`. When the offset changes, polling skips or adds ticks so the local input timeline has no holes or
duplicates.

**Time sync.** Every server input packet carries `{MaxPing, ServerTime, ServerTimeScale}`. On start the client
fast-forwards to the server tick. `PerformClockAdjustment` averages 16 samples of local time against
`serverTime + RTT/2` and nudges the clock by 1/16 of the error when it exceeds a hard-coded 0.017 s (a 60 Hz
assumption). `ServerTimeScale` dilates time for high-ping sessions.

**Late input.** Per the `InputHardTolerance` doc, the server waits that many ticks, then replaces missing input with
repeated input or nulls (`ReplacedByServer`). Input flags: `Repeatable = 1`, `PlayerNotPresent = 2`,
`ReplacedByServer = 4`, `Command = 8`.

**Wire format.** Event 100 reliable protocol messages; 101 unreliable RTT; 102 client input, each input repeated in the
next `InputRedundancy` packets; 103 the server's per-tick input sets, delta-compressed against the previous tick
(changed 32-bit words as varint index plus zigzag difference). Input payloads are at most 255 bytes.

## Input and commands

**Input** is declared in the DSL (`input { button Fire; FPVector2 Direction; }`). Code generation stores the input of
every player inside the frame globals, so input is part of the rolled-back state. `Button` keeps
`{frameUp, frameDown, frameCurrent}`: 12 bytes in memory, 1 bit on the wire, making `IsDown`, `WasPressed` and
`WasReleased` rollback-correct.

```csharp
QuantumCallback.Subscribe(this, (CallbackPollInput c) =>
{
    var input = new Quantum.Input();
    input.Fire = UnityEngine.Input.GetButton("Fire1");
    c.SetInput(input, DeterministicInputFlags.Repeatable);
});

// In a system:
Input* input = f.GetPlayerInput(playerRef);
```

**Commands** (`DeterministicCommand` with `Serialize(BitStream)`, registered in a command factory) are sent reliably
with `game.SendCommand(slot, command)`. Locally they are injected at the next predicted tick; the server puts them into
an upcoming tick with the `Command` flag. Systems read `f.GetPlayerCommand(player)`. Commands are not repeated during
prediction.

**Players.** Clients own local *slots*; the server assigns global `PlayerRef`s; sessions start as spectators.
`game.AddPlayer(slot, RuntimePlayer)` sends serialized player data, which the server injects into the input stream;
`Frame.UpdatePlayerData` applies it **only on verified frames** and raises `ISignalOnPlayerAdded`.
`PlayerConnectedSystem` raises connect/disconnect signals from `PlayerNotPresent` changes on verified frames.

## Frames, snapshots and checksums

**State.** A `Frame` holds the generated globals struct (RNG, player input, `DeltaTime`, map, system-enabled bits,
physics state, user globals), a sparse-set ECS with block-packed component buffers, a page-based heap for frame
collections (`QList`, `QDictionary`, `QHashSet`; pointers are offsets), a dynamic asset database and the runtime
player map. Because pointers are offsets, a frame copy is a set of memcpys and `EntityRef`s stay valid.

**Four frames** are published to the view: `Verified`, `Predicted`, `PredictedPrevious` (copied before the newest tick,
for interpolation) and `PreviousUpdatePredicted` (the corrected state at the tick shown last update, for smoothing
mispredictions).

**Serialization.** `Frame.Serialize` writes mode, number, checksum, runtime players, the frame base, globals,
entities and the dynamic asset DB, GZip-compressed; `Deserialize` re-verifies the checksum.

**Late join.** The server asks an existing client for a snapshot of its verified frame ("buddy snapshot"), which is
sent in 48 KB chunks; the joining client resyncs from it.

**Checksums.** `Frame.CalculateChecksum` runs the frame serializer into a CRC64 stream seeded with the tick number,
covering globals (including input), entities, components and heap data. It runs on verified frames where
`Number % ChecksumInterval == 0` and is sent to the server. On a mismatch the server sends `TickChecksumError`, the
session stops and `CallbackChecksumError` fires; every client then uploads a frame dump (configs, the verified frame
from a 3-second ring buffer, asset CRCs) for the Frame Differ tool.

## Determinism techniques

**`FP`** (`Photon.Deterministic.FP`): `long RawValue` with 16 fractional bits (`Precision = 16`,
`RAW_ONE = 65536`).

```csharp
a * b => (a.RawValue * b.RawValue + 32768) >> 16    // rounded, unchecked: keep magnitudes below ~32768
a / b => (a.RawValue << 16) / b.RawValue
[Obsolete("Don't cast from float to FP", true)] public static implicit operator FP(float v) => throw ...;
```

Implicit conversion from `float` is a compile error. Constants are stored as raw values (`FP.Raw._0_10`),
`FP.FromString` parses decimals deterministically, `FromFloat_UNSAFE` exists for edit time. Also `FPVector2/3`,
`FPQuaternion`, `FPMatrix*`, `FPBounds*`, `IntVector2/3`.

**Lookup tables** (`Runtime/RuntimeAssets/LUT/*.bytes`, loaded by `FPLut.Init` before any simulation): sin/cos with one
entry per raw step in [0, 2π) (411 775 longs), tan, asin/acos (131 073 entries each), sqrt (65 537 entries with extra
precision bits, larger inputs normalized by shifting); log2 and exp use small generated tables.

**RNG** (`RNGSession`): PCG32 XSH-RR, seeded through SplitMix64 from the runtime config seed, stored in the globals, so
it rolls back with the frame. Bounded integers use unbiased rejection sampling.

**Collections and hashing.** Collections live on the frame heap. Generated types get explicit `GetHashCode`
implementations (`hash * 31 + field`), and the changelog lists desync fixes for non-deterministic `GetHashCode`
implementations, which shows how easy that mistake is.

**Rules they document.** "Systems must be stateless... Quantum only guarantees determinism if all game state data is
fully contained in the Frame". Static fields are audited with analyzer attributes. Using `IsLocalPlayer` for state
"will lead to desyncs". Destroys and component removals are deferred to fixed commit points.

**Platform.** No Burst: the simulation is unsafe C# running identically on Mono, IL2CPP and .NET (a .NET console
runner exists). `MemoryLayoutVerifier` checks the field offsets and sizes of every registered type at start and throws
on mismatch. Threaded systems split component buffers into slices and may use only a thread-safe frame API.

**Prediction culling.** `f.SetPredictionArea(position, radius)`: entities outside the area are simulated only on
verified ticks.

## Systems, signals and events

- Systems: `SystemMainThread`, `SystemMainThreadFilter<T>`, `SystemSignalsOnly`, `SystemGroup`,
  `SystemThreadedFilter<T>`; configured in a `SystemsConfig` asset. Enabled flags live in the frame, so toggling a
  system rolls back too.
- Tick order: apply input, simulate begin, player data (verified only), systems, commit, simulate finished.
- Signals: DSL `signal Foo(...)` generates `ISignalFoo`; `f.Signals.Foo()` calls every enabled implementer.
- Events: `event`, `synced event` (raised only on verified frames), `client`/`server` modifiers, `nothashed` fields.
  Non-synced events are deduplicated by `(tick, id, hash)` across re-simulations and later confirmed or cancelled
  (`CallbackEventConfirmed` / `CallbackEventCanceled`).

## Presentation

- `QuantumRunnerBehaviour.Update` services the session from Unity's `Update` at a variable rate.
- After the update the game publishes the four frames, sets `InterpolationFactor = clamp01(accumulatedTime * rate)`,
  fires `CallbackUpdateView` and then events.
- `QuantumEntityViewUpdater` creates and destroys GameObjects for entities with a `View` component, for verified and
  predicted frames.
- Transform interpolation between `PredictedPrevious` and `Predicted`, plus misprediction smoothing: the difference
  between the previously shown position and the corrected one is added as an error offset that decays (blend between
  0.25 m and 1 m, teleport above 2 m; rotation thresholds 0.1 / 0.5 rad). An optional snapshot-interpolation mode plays
  verified transforms about 4 ticks behind.
- Callbacks: poll input, game init/started/resynced/destroyed, update view, simulate finished, event
  confirmed/canceled, checksum error/frame dump/computed, input confirmed, plugin disconnect, local player add/remove.

## Assets and configuration

- `AssetObject` is a ScriptableObject in Unity and a plain class in .NET, identified by a deterministic 64-bit
  `AssetGuid`. Components store `AssetRef<T>`; systems resolve it with `f.FindAsset`. Assets are read-only; runtime
  assets go into the frame's dynamic asset DB.
- `RuntimeConfig` (seed, map, simulation and systems config, user fields) travels through the server, which echoes the
  first client's bytes so every client deserializes the same data. `SimulationConfig` holds physics, navigation,
  threading and checksum history settings.

## Local mode, replays and saves

- Game modes: Multiplayer, Local, Replay. In local mode polled input is immediately verified, so nothing is
  re-simulated and the protocol runs in-process.
- Replays force lockstep mode and read input from a replay provider. Recording keeps the raw server input packets (or
  the confirmed inputs); a replay file holds the session config, runtime config, input history, initial tick or frame,
  checksums and the asset DB, and playback can verify the checksums (also headless).
- Save games serialize the verified frame plus configs; instant replays keep a ring of snapshots plus input.

## Strengths

1. The server only orders input; joins, leaves and commands travel inside the input stream, so they apply on the same
   tick everywhere.
2. Always re-simulate on confirmation, with a hard rollback window: predictable worst case, stall instead of
   over-predicting.
3. Everything that influences the simulation lives in the frame: input, button edges, RNG, system flags, delta time.
4. Four frames give interpolation and misprediction smoothing cheaply.
5. Event lifecycle with confirmation/cancellation; `synced` and `nothashed` modifiers.
6. Adaptive input delay from RTT and server-driven time dilation.
7. Desync tooling: checksums on verified frames, automatic frame dumps with asset CRCs, a frame differ, replays that
   verify checksums.
8. Compile-time protection: a compile error on `float` to `FP`, integer-only trigonometry, generated deterministic
   `GetHashCode`, memory layout verification.

## Weaknesses and pitfalls

1. Late join depends on a snapshot that contains *everything*; the changelog lists many late-join desyncs caused by
   state missing from it (physics callbacks, toggled colliders, system toggles).
2. The clock correction threshold is hard-coded for 60 Hz; an advanced time provider ships but its result is unused.
3. The checksum serializer is not thread-safe; the cross-platform checksum flag exists but is unused.
4. `FP` multiplication is unchecked: only values within `FP.UseableMin..UseableMax` (±32 768) are safe to multiply.
   This is the trade-off of any 64-bit Q16 format (the `FixedPoint` of this package has the same limit on products).
5. Lookup tables (several MB) must be loaded before the simulation runs.
