# Changelog

All notable changes to this package are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-10-03

### Added

- Deterministic math: `FixedPoint` (Q47.16 fixed point), `FixedVector2`, `FixedVector3`, `FixedQuaternion` and `FixedMath`, with integer-only
  `Sqrt`, trigonometry, `Exp`/`Log`/`Pow`, exact decimal parsing and Burst-identical results; `FixedRandom` (PCG32).
- `LockstepSimulation`: an isolated Entities world stepped one confirmed frame at a time, with
  `LockstepSimulationSystemGroup`, begin/end command buffer systems, `LockstepTime`, `LockstepSessionInfo`,
  `LockstepRandom`, player entities (`LockstepPlayer`, `LockstepPlayerInput`, `LockstepCommand`,
  joined/left flags), `LockstepTransform` with interpolation history and `LockstepEntityId`.
- `LockstepServer`: transport-agnostic session authority. Orders inputs, closes ticks on its own clock, repeats a
  late player's previous input, never drops commands, streams frames, measures input timing, compares checksums by
  majority and reports desyncs; optional wait for late inputs; late join by replaying the match; server-side replays.
- `LockstepClient`: input clock steered by server feedback, jitter-aware playout buffer with catch-up budget,
  checksum reporting, desync events, replay export.
- `LockstepChecksum`: state hash over every unmanaged component, independent of padding, type indices and entity ids,
  with a per-type breakdown for desync diagnosis.
- Netcode for Entities integration: one manually serialized RPC, server and client systems driven by
  `LockstepServerConfig` / `LockstepClientConfig`, thin client support, `LockstepNetcode` helpers.
- Offline mode (`LockstepOfflineConfig`) running the same protocol over an in-process loopback.
- `LockstepReplay` / `LockstepReplayPlayer` with checksum verification.
- Presentation: `LockstepInputSystemGroup` and `LockstepLocalInput`, `LockstepViewSystem` mirroring the simulation
  into entity views from a `LockstepPrefabRegistry`.
- Authoring: `LockstepTransformAuthoring`, `LockstepPrefabRegistryAuthoring`, `LockstepEntityIdAuthoring`.
- Editor: inspectors for fixed-point fields, *Window > Pragma > Lockstep Sessions* debug window.
- Claude Code skills in `Skills~`: `pragma-lockstep-gameplay`, `pragma-lockstep-sessions`,
  `pragma-lockstep-desync`.
