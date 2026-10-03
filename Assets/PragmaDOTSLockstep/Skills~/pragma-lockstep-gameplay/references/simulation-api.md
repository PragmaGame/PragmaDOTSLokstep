# Simulation-side API reference

Namespace `Pragma.Lockstep` (assembly `Pragma.Lockstep`) unless noted. Fields of components are public and
camelCase; settings and options are PascalCase properties.

## Contents

- System group and order
- Singletons of the simulation world
- Player entities
- Input and commands
- Transforms
- Entity ids
- Prefab registry
- Navigation
- Stats
- Bytes helpers
- Presentation-side access
- Authoring components

## System group and order

| Type | Notes |
|---|---|
| `LockstepSimulationSystemGroup` | Root group of the simulation world, updated once per confirmed tick. Members inherit `LockstepWorldFilter.SIMULATION` |
| `LockstepWorldFilter.SIMULATION` | Custom `WorldSystemFilterFlags` bit (1 << 28) for systems that cannot live inside the group |
| `LockstepFrameApplySystem` | `OrderFirst`. Destroys players that left last tick, moves input to previous, clears command buffers, disables the joined flag, applies the frame |
| `LockstepTransformHistorySystem` | `OrderFirst`, after frame apply. Copies `LockstepTransform` into `LockstepTransformPrevious` |
| `LockstepBeginSimulationEntityCommandBufferSystem` | `OrderFirst`, after the transform history. Singleton: `LockstepBeginSimulationEntityCommandBufferSystem.Singleton` |
| gameplay systems | Default order slot inside the group |
| `LockstepEndSimulationEntityCommandBufferSystem` | `OrderLast`. Singleton: `LockstepEndSimulationEntityCommandBufferSystem.Singleton` |
| `LockstepEntityIdSystem` | `OrderLast`, after the end buffer. Assigns ids, rebuilds `LockstepEntityIdMap` |

Command buffer from a system:

```csharp
var buffer = SystemAPI.GetSingleton<LockstepEndSimulationEntityCommandBufferSystem.Singleton>()
    .CreateCommandBuffer(state.WorldUnmanaged);
// In a parallel IJobEntity: buffer.AsParallelWriter(), and use [ChunkIndexInQuery] int sortKey in Execute.
```

`LockstepSimulationOptions` (when creating `LockstepSimulation`, `LockstepClient` or `LockstepReplayPlayer`
yourself): `WorldName`, `AutoDiscoverSystems` (default true), `AdditionalSystems` (`IReadOnlyList<Type>`),
`Initialize` (`Action<World>`, runs once before tick 0), `CanCreate` (`Func<bool>`, gate before creation).

## Singletons of the simulation world

| Component | Members |
|---|---|
| `LockstepTime` | `int tick` (the tick being simulated, from 0), `int tickRate`, `FixedPoint deltaTime` (1 / tickRate, truncated), `FixedPoint elapsedTime` (tick / tickRate) |
| `LockstepSessionInfo` | `uint seed`, `int tickRate`, `int maxPlayers`, `int inputSize`, `FixedList128Bytes<byte> startData`, `T GetStartData<T>()` |
| `LockstepRandom` | `FixedRandom value` - take it by `ref` |
| `LockstepPlayerSlot` (buffer) | `Entity player` per slot, `Entity.Null` when free |
| `LockstepEntityIdCounter` | `uint last` |
| `LockstepEntityIdMap` | `NativeHashMap<uint, Entity> map`, `TryGetEntity(uint, out Entity)`, `TryGetEntity(LockstepEntityId, out Entity)`; ignored by the checksum |
| `LockstepFrameData` (buffer) | `byte value`: the raw frame of the current tick (rarely needed) |
| `LockstepPrefabRegistry` + `LockstepPrefabElement` (buffer) | Copied from the presentation world when the simulation is created; empty when it had no baked registry yet |

## Player entities

Created by `LockstepFrameApplySystem` on the join tick.

| Component | Members |
|---|---|
| `LockstepPlayer` | `int slot`, `int joinTick`, `FixedList64Bytes<byte> joinData`, `T GetJoinData<T>()` |
| `LockstepPlayerInput` | `int size`, `T Get<T>()`, `T GetPrevious<T>()`, `bool HasChanged()`; `CAPACITY` = 128 |
| `LockstepCommand` (buffer) | Commands confirmed for this tick, cleared at the start of the next |
| `LockstepPlayerJoined` (enableable) | Enabled only on the join tick |
| `LockstepPlayerLeft` (enableable) | Enabled only on the leave tick; the entity is destroyed at the start of the next tick |

Queries over enableable components filter on the enabled state by default:
`SystemAPI.Query<RefRO<LockstepPlayer>>().WithAll<LockstepPlayerJoined>()` returns only players that joined this tick.

## Input and commands

Client (presentation world):

| API | Notes |
|---|---|
| `LockstepInputSystemGroup` | Client/local world group for input systems, before the session update |
| `LockstepLocalInput` (singleton) | `Set<T>(in T)`, `Get<T>()`, `int size`; same entity holds the `LockstepCommand` and `LockstepCommandData` buffers for outgoing commands |
| `LockstepClient.SetInput<T>(in T)`, `AddCommand<T>(in T)`, `AddCommand<T, TData>(in T, NativeArray<TData>)` | The same from outside ECS |

`LockstepCommand`:

| Member | Notes |
|---|---|
| `static LockstepCommand Create<T>(in T payload)` | Payload: unmanaged struct, at most `MAX_PAYLOAD_SIZE` (122) bytes |
| `static LockstepCommand Create<T, TData>(in T payload, NativeArray<TData> data, DynamicBuffer<LockstepCommandData> dataBuffer)` | Plus data of any length, appended to the `LockstepCommandData` buffer of the entity the command is added to |
| `bool Is<T>()`, `bool TryGet<T>(out T)`, `T Get<T>()` | Decode the payload in the simulation |
| `NativeArray<T> GetData<T>(DynamicBuffer<LockstepCommandData>)` | The data as `T` elements: a view valid until the buffer changes; trailing bytes short of a `T` are ignored |
| `int DataLength` | Data size in bytes, 0 without data |
| `static int TypeHashOf<T>()` | Stable 32-bit hash of the type's full name |
| `int typeHash`, `byte size` | Raw fields |

`LockstepCommandData` (buffer, `byte value`): the data of the commands of the same entity, back to back, each
starting on an 8-byte boundary; on player entities and the local input entity, cleared with the commands.

`LockstepProtocol` limits: `MAX_PLAYERS` 64, `MAX_INPUT_SIZE` 128, `MAX_COMMANDS_PER_TICK` 32 (per player; the rest
moves to later ticks), `MAX_JOIN_DATA_SIZE` 62, `MAX_START_DATA_SIZE` 126.

## Transforms

| Type | Members |
|---|---|
| `LockstepTransform` | `FixedVector3 position`, `FixedQuaternion rotation`, `FixedPoint scale`; `Identity`, `FromPosition`, `FromPositionRotation`, `Forward`, `Up`, `Right`, `TransformPoint`, `InverseTransformPoint`, `TransformDirection`, `ToLocalTransform()` (render only) |
| `LockstepTransformPrevious` | `position`, `rotation`, `scale`, `capturedTickPlusOne`, `IsCapturedAt(int tick)`; written at the start of every tick |
| `LockstepTransformExtensions.Interpolate(in LockstepTransform, in LockstepTransformPrevious, int lastSimulatedTick, float alpha)` | `LocalTransform` for rendering; current value when the capture is not from `lastSimulatedTick` |
| `simulationManager.TryGetInterpolated(Entity, int lastSimulatedTick, float alpha, int shownSinceTick, out LocalTransform)` | Reads both components and interpolates; the current value until the tick after `shownSinceTick` (when the view appeared); false without `LockstepTransform` |

## Entity ids

| Type | Notes |
|---|---|
| `LockstepEntityId` | `uint value`, `bool IsAssigned`. Add with value 0; assigned at the end of the creation tick, from 1, never reused |
| `LockstepEntityIdMap` | Singleton (on a system entity, so use `SystemAPI.GetSingleton`, or a query with `EntityQueryOptions.IncludeSystems`). Rebuilt at the end of every tick: an entity created on tick T resolves from tick T + 1 |
| `Pragma.Lockstep.Authoring.LockstepEntityIdAuthoring` | Bakes a `LockstepEntityId` onto a prefab |

## Prefab registry

| Type | Notes |
|---|---|
| `Pragma.Lockstep.Authoring.LockstepPrefabRegistryAuthoring` | Lists prefabs; put it in a subscene of the client/local world. The list index is the prefab id |
| `LockstepPrefabRegistry`, `LockstepPrefabElement { Entity prefab }` | Copied into every simulation world under the same indices |
| `LockstepPrefabId { int value }` | Added to the copied prefabs, so every instance carries its registry index |
| `LockstepPrefabUtility.HasRegistry(EntityManager)`, `CopyRegistry(source, destination)` | Plumbing used by the built-in systems |

Spawning: `state.EntityManager.Instantiate(SystemAPI.GetSingletonBuffer<LockstepPrefabElement>(true)[index].prefab)`.
Scene entities, the objects a map starts with:

| Type | Notes |
|---|---|
| `Pragma.Lockstep.Authoring.LockstepSceneEntityAuthoring` | Put it on a scene object of the subscene that holds the registry: the object is copied into every simulation world before tick 0 |
| `LockstepSceneEntity { ulong order }` | Copy order, baked from the object's identity in its scene; the copy keeps the component |
| `LockstepPrefabUtility.CopySceneEntities(source, destination)` | The copy the built-in systems make after `CopyRegistry`; references to anything outside the scene entities (registry prefabs included) become `Entity.Null`, so name prefabs by index or by a key |

The registry entity always exists in simulation worlds made by the built-in systems, so check the buffer length before
indexing, and use `waitForPrefabRegistry` when starting the session so the simulation is created after the subscene
loaded.

## Navigation

Namespace and assembly `Pragma.Lockstep.Navigation`.

| Type | Members |
|---|---|
| `LockstepNavGrid` | Singleton. `FixedVector2 origin` (corner of cell 0, 0 on X and Z), `FixedPoint cellSize`, `int width`, `int height`, `FixedPoint agentRadius`, `uint version` (changes with any cell); `CellCount`, `IsValid`, `Contains`, `GetIndex`, `GetCell`, `WorldToCell`, `GetCellCenter`, `ClampToGrid` |
| `LockstepNavCell` | Buffer on the grid entity, row by row along X: `ushort blockers`, `IsWalkable`. Sized by `LockstepNavObstacleSystem` |
| `LockstepNavObstacle` | `FixedVector2 center`, `FixedVector2 size`: a rectangle in the space of the entity's `LockstepTransform` (yaw, uniform scale), world space without one |
| `LockstepNavObstacleFootprint` | Cleanup component the obstacle system writes: the stamped world rectangle (`center`, `right`, `halfSize`, `Forward`); `Create(obstacle, transform)`, `Create(obstacle)` |
| `LockstepNavAgent` | `speed`, `angularSpeed` (radians per second, 0 turns at once), `stoppingDistance`, `destination`, `status`, `isPathPartial`, `waypointIndex`, `gridVersion`; `SetDestination(FixedVector3)`, `Stop()`, `IsMoving` |
| `LockstepNavStatus` | `Idle`, `Requested` (planned in the next navigation update), `Moving`, `Arrived` |
| `LockstepNavWaypoint` | Buffer: `FixedVector3 position`; the path, end included, Y of the destination |
| `LockstepNavSystemGroup` | In `LockstepSimulationSystemGroup`: `LockstepNavObstacleSystem` (first), `LockstepNavPathSystem`, `LockstepNavMoveSystem`. Set destinations before it |
| `LockstepPathfinder` | `new LockstepPathfinder(cellCount, allocator)`, `FindPath(grid, cells, start, destination, NativeList<FixedVector3> or DynamicBuffer<LockstepNavWaypoint>)` returns `LockstepPathStatus` (`Failed`, `Complete`, `Partial`), `Dispose()`. Keeps scratch memory; one per thread |
| `LockstepNavigation` | `IsWalkable(grid, cells, cell or position)`, `HasLineOfSight(grid, cells, from, to)` (exact, corners count both sides), `TryFindNearestWalkable(grid, cells, cell, out nearest)`, `Stamp(grid, cells, footprint, count)` for previews and tests |

Read the cells with `SystemAPI.GetSingletonBuffer<LockstepNavCell>(true).AsNativeArray()` and the grid with
`SystemAPI.GetSingleton<LockstepNavGrid>()`.

## Stats

Namespace and assembly `Pragma.Lockstep.Stats`.

| Type | Members |
|---|---|
| `LockstepStat` | Buffer: `int type` (the game's stat id), `FixedPoint baseValue`, `FixedPoint value` (base with the modifiers applied); `Create(type, baseValue)`. Each stat once per entity |
| `LockstepStatModifier` | Buffer: `int stat`, `LockstepStatModifierType type`, `FixedPoint value`, `LockstepStatSource source`, `int endTick` (`PERMANENT` = 0: until removed); `Flat`, `Additive`, `Multiplicative` factories `(stat, value, source = default, endTick = PERMANENT)`, `IsExpired(tick)` |
| `LockstepStatModifierType` | `Flat` (adds), `Additive` (shares summed: 0.25 adds 25 %), `Multiplicative` (times 1 + value, stacking) |
| `LockstepStatSource` | `uint kind`, `uint id`, equality. What applied a modifier; one source's modifiers are removed together |
| `LockstepStatSystem` | In `LockstepSimulationSystemGroup`: removes expired modifiers, recalculates entities with both buffers whose chunk changed, writes `LockstepStat` only then |
| `LockstepStats` | `TryGetValue(stats, type, out value)`, `TryGetBase`, `TrySetBase(stats, type, baseValue)`, `RemoveModifiers(modifiers, source)` (returns the count, keeps the order), `Calculate(type, baseValue, NativeArray<LockstepStatModifier>)` |

`value = (base + sum of Flat) * (1 + sum of Additive) * product of (1 + Multiplicative)`. A modifier ending on tick
T + D was applied on ticks T to T + D - 1 when added on tick T before `LockstepStatSystem`.

## Bytes helpers

`LockstepBytes.ToFixedList64(in T)`, `ToFixedList128(in T)`, `Read<T>(in FixedList64Bytes<byte>)`,
`Read<T>(in FixedList128Bytes<byte>)` - for join data and start data structs.

## Presentation-side access

| API | Notes |
|---|---|
| `LockstepWorlds.TryGetClient(World, out LockstepClient)` | The session of a client or offline world |
| `LockstepWorlds.TryGetSimulation(World, out LockstepSimulation)` | Its simulation, once created |
| `LockstepWorlds.TryGetServer(World, out LockstepServer)` | Server of a server world, or the in-process server of an offline world |
| `LockstepWorlds.GetClients(list)`, `GetServers(list)` | All sessions of the process |
| `LockstepSimulation.World`, `.Tick` (simulated ticks = next tick), `.Config`, `.ComputeChecksum()` | Read only |
| `LockstepClient.InterpolationAlpha` | Blend factor in `[0, 1]` |
| `LockstepViewSystem` (presentation worlds) | Mirrors entities with `LockstepPrefabId` as rendered registry prefabs; `LockstepView { Entity simulationEntity }`, `TryGetView`, `ViewCount` |
| `EntityViewManager.TryGet(World, out manager)` (`Pragma.Lockstep.Views`) | GameObject views of the entities with an `EntityViewKey`: `TryGetView`, `Attach`, `Detach`, `ForceUpdate`; see `views.md` |

## Authoring components

Namespace `Pragma.Lockstep.Authoring`:

- `LockstepTransformAuthoring` - bakes the transform into `LockstepTransform` (float to `FixedPoint` once, at bake time); *Interpolate*
  adds `LockstepTransformPrevious`.
- `LockstepPrefabRegistryAuthoring` - the prefab list.
- `LockstepSceneEntityAuthoring` - copies a scene object into every simulation world before tick 0.
- `LockstepEntityIdAuthoring` - adds `LockstepEntityId`.
- `EntityViewKeyAuthoring` - adds `EntityViewKey`, so instances get the GameObject view bound to the key.
- `EntityViewConfigAuthoring` - bakes an `EntityViewConfig` catalog into the presentation world (not for servers).
- `LockstepNavGridAuthoring` - the navigation grid of a map (size, cell size, agent radius); needs and adds
  `LockstepSceneEntityAuthoring`. Selecting it previews the blocked cells.
- `LockstepNavObstacleAuthoring` - a box whose X and Z block the grid; moves with a `LockstepTransformAuthoring` next to
  it, otherwise its pose is baked (static map content, add `LockstepSceneEntityAuthoring`).
- `LockstepNavAgentAuthoring` - `LockstepNavAgent` and its waypoint buffer; needs `LockstepTransformAuthoring`.

For your own data, write regular bakers and convert floats to `FixedPoint` in the baker (`(FixedPoint)authoring.speed`): baking
happens once, so every client loads the same raw values.
