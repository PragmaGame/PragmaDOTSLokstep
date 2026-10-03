# Changelog

All notable changes to this package are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- GameObject views (`Pragma.Lockstep.Views`), adapted from ECV (Entity Component View): `EntityView` prefabs with
  one part per component type (`EntityComponentView<T>`, `EntityComponentViewUnmanaged<T>`, `TransformComponentView`),
  spawned and returned by `EntityViewManagerSystem` for the simulation entities with an `EntityViewKey`, fed by
  `EntityViewUpdateSystem<T>` when their component changes and by `TransformViewUpdateSystem` with interpolated
  transforms. Nothing is written to the simulation world.
- View catalogs: `EntityViewConfig`, baked with `EntityViewConfigAuthoring` (skipped for dedicated servers) or
  registered at runtime with `EntityViewConfigProvider` / `EntityViewConfigs`; `EntityViewKeyAuthoring` bakes keys.
- Pool abstraction: `IEntityViewPool`, chosen per project with `EntityViewManagerSystem.PoolFactory` or per world with
  `EntityViewManagerSystem.Pool`; `EntityViewPool` is the default.
- `EntityViewManager`: views by entity, `Attach`/`Detach` for views the caller owns (HUD panels), `ForceUpdate`.
- `EntityView.TryGetData<T>`: reads a component of the entity shown from the simulation world (its `LockstepEntityId`
  for commands).
- `LockstepTransformExtensions.TryGetInterpolated`: reads and interpolates the transform of a simulation entity with the
  rule both view systems use.
- The sample shows its avatars and projectiles with GameObject views.
- Scene entities: `LockstepSceneEntityAuthoring` marks the objects of a subscene that every simulation world starts
  with (buildings, resource nodes, spawn markers). `LockstepClientWorldUtility.CreateSimulationOptions` copies them,
  with their linked entities, after the prefab registry and before tick 0, in an order baked from the objects'
  identities (`LockstepSceneEntity.order`), so the result does not depend on the order subscenes loaded in.
  `LockstepPrefabUtility.CopySceneEntities` does the copy for custom flows.
- Navigation (`Pragma.Lockstep.Navigation`): a walkability grid (`LockstepNavGrid` with its `LockstepNavCell`
  buffer), obstacles (`LockstepNavObstacle`) that block cells while their entity exists, A* with string pulling in
  integer math (`LockstepPathfinder`, queries in `LockstepNavigation`) and agents (`LockstepNavAgent`,
  `LockstepNavWaypoint`) that walk their paths in `FixedPoint`, plan in parallel and plan again when an obstacle
  blocks their way. `LockstepNavSystemGroup` runs `LockstepNavObstacleSystem`, `LockstepNavPathSystem` and
  `LockstepNavMoveSystem`; `LockstepNavGridAuthoring`, `LockstepNavObstacleAuthoring` and `LockstepNavAgentAuthoring`
  bake them, and selecting a grid previews the cells the obstacles block.
- Command data: besides its payload struct (still up to 122 bytes) a command carries data of any length, an array of
  any unmanaged element type, for lists such as the unit ids of an order. `LockstepCommand.Create(payload, data,
  dataBuffer)` writes it into the new `LockstepCommandData` buffer, which the local input entity and every player
  entity have next to their `LockstepCommand` buffer; `LockstepCommand.GetData<T>(dataBuffer)` reads it as a view and
  `DataLength` gives its size. Outside ECS, `LockstepClient.AddCommand(payload, data)`. Large commands are fragmented on
  the wire and reach every client in one tick.

### Changed

- Protocol version 2: a command on the wire ends with its data length and data. Clients, servers and replays of
  version 1 are refused.
- `LockstepClient` and `LockstepServer` queue commands in their wire form; the server checks the form and relays the
  bytes without decoding them.

### Removed

- `LockstepClient.AddCommand(in LockstepCommand)`: use `AddCommand<T>(payload)`, `AddCommand<T, TData>(payload, data)`
  or the `LockstepCommand` buffer of the local input entity.

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
