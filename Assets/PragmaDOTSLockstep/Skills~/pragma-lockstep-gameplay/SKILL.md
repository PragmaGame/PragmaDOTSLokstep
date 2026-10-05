---
name: pragma-lockstep-gameplay
description: Write deterministic gameplay for Pragma DOTS Lockstep (com.pragma.dotslockstep, Pragma.Lockstep) - simulation systems in LockstepSimulationSystemGroup, input structs and commands, player join/leave, spawning, LockstepEntityId references, grid navigation (LockstepNavGrid, LockstepNavAgent, LockstepPathfinder), stats (attributes with timed or non-stacking modifiers, resources such as health with damage, healing and caps, research and squad bonuses granted to groups: LockstepStat, LockstepStatModifier, LockstepStatChange, LockstepStatGrant), fixed-point math (FixedPoint, FixedVector3, FixedMath, FixedRandom) and presenting it with interpolation and GameObject views (EntityView, EntityComponentView, EntityBufferView, update systems, catalogs, pools). Use it whenever you add or change anything that runs inside a lockstep simulation world or reads it for rendering - units, movement, combat, abilities, economy, AI, timers, randomness, input, views, UI - even if the user never says "deterministic" or "lockstep".
---

# Gameplay with Pragma DOTS Lockstep

Every client runs the same simulation in its own Entities world and steps it with the same confirmed frames (one per
tick). The machines stay identical only if the simulation code is deterministic: a single `float` operation, a sort by
`Entity` or a `static` counter can make one client compute something different, and from then on the match is
desynced for good. Treat the rules below as correctness requirements, not style.

Deeper reference:

- `references/simulation-api.md` - every simulation-side type, singleton and helper.
- `references/fixed-point.md` - `FixedPoint` semantics, `FixedMath`, vectors, quaternions, `FixedRandom`.
- `references/views.md` - GameObject views: keys, parts, update systems, catalogs, the manager, pools.
- The package README: `Assets/PragmaDOTSLockstep/README.md` in the package repository, or
  `Library/PackageCache/com.pragma.dotslockstep@*/README.md` in a project that installed it.
- The `pragma-lockstep-desync` skill when something already diverged; `pragma-lockstep-sessions` for networking.

## Where code goes

| Code | World | How |
|---|---|---|
| Gameplay rules, state changes | simulation world | `[UpdateInGroup(typeof(LockstepSimulationSystemGroup))]` |
| Reading keyboard, gamepad, UI | client or local world | `[UpdateInGroup(typeof(LockstepInputSystemGroup))]`, write `LockstepLocalInput` |
| Rendering, UI, audio, camera | presentation world | read `client.Simulation.World`, never write to it |

Systems in `LockstepSimulationSystemGroup` are created only in simulation worlds (the group carries a custom
`WorldSystemFilter` flag that its members inherit). A gameplay system outside the group does not run in the
simulation at all; a presentation system inside the group would run in the simulation.

## Workflow for a new mechanic

1. **State.** Put it in unmanaged `IComponentData` / `IBufferElementData` with `FixedPoint`, `FixedVector2`, `FixedVector3`, `FixedQuaternion`,
   integer and `bool` fields. Durations are tick counts (`int`), positions are `LockstepTransform`.
2. **Who drives it.** Continuous player control (movement, aiming, held buttons) belongs in the input struct;
   discrete actions that must never be lost (build, buy, cast, chat) are commands; everything else is driven by
   simulation state and `LockstepTime`.
3. **System.** Add an `ISystem` (Burst-compiled where possible) to `LockstepSimulationSystemGroup`, ordered against
   the other gameplay systems with `[UpdateBefore]` / `[UpdateAfter]`.
4. **Structural changes.** Never create or destroy entities while iterating a `SystemAPI.Query`: record into a
   lockstep command buffer or collect into a `NativeList` and apply after the loop.
5. **Presentation.** Read the new state from the presentation side (views, UI, effects).
6. **Verify.** Compile, run the session offline, and run the determinism tests (see `pragma-lockstep-desync`): at
   least two clients in one process with checksums on.

## The simulation API you need most

```csharp
// Clock and session.
var time = SystemAPI.GetSingleton<LockstepTime>();          // tick, tickRate, deltaTime (FixedPoint), elapsedTime (FixedPoint)
var info = SystemAPI.GetSingleton<LockstepSessionInfo>();   // seed, tickRate, maxPlayers, inputSize, GetStartData<T>()

// Randomness: take a ref, or the drawn numbers do not advance the stored generator.
ref var random = ref SystemAPI.GetSingletonRW<LockstepRandom>().ValueRW.value;
var roll = random.NextInt(1, 7);

// Command buffers played back inside the tick.
var buffer = SystemAPI.GetSingleton<LockstepEndSimulationEntityCommandBufferSystem.Singleton>()
    .CreateCommandBuffer(state.WorldUnmanaged);

// Registry prefabs (baked LockstepPrefabRegistryAuthoring, copied into every simulation world; empty if the
// subscene had not loaded when the simulation was created, so start with waitForPrefabRegistry).
var prefabs = SystemAPI.GetSingletonBuffer<LockstepPrefabElement>(true);
var prefab = prefabIndex < prefabs.Length ? prefabs[prefabIndex].prefab : Entity.Null;

// Map objects (buildings, markers) placed in that subscene with LockstepSceneEntityAuthoring are already in the
// simulation world at tick 0: query them like any entity (they keep LockstepSceneEntity).

// Cross-client identities.
var ids = SystemAPI.GetSingleton<LockstepEntityIdMap>();
ids.TryGetEntity(targetId, out var target);
```

Order inside the group: `LockstepFrameApplySystem` -> `LockstepTransformHistorySystem` ->
`LockstepBeginSimulationEntityCommandBufferSystem` -> your systems -> `LockstepEndSimulationEntityCommandBufferSystem`
-> `LockstepEntityIdSystem`.

## Players

The frame apply system creates one entity per player on the join tick, with `LockstepPlayer` (`slot`, `joinTick`,
`joinData`, `GetJoinData<T>()`), `LockstepPlayerInput`, the `LockstepCommand` and `LockstepCommandData` buffers and
two enableable flags:
`LockstepPlayerJoined` (enabled only on the join tick) and `LockstepPlayerLeft` (enabled only on the leave tick; the
entity is destroyed at the start of the next tick).

```csharp
// Join tick: spawn. Leave tick: clean up everything the player owned.
var buffer = SystemAPI.GetSingleton<LockstepEndSimulationEntityCommandBufferSystem.Singleton>()
    .CreateCommandBuffer(state.WorldUnmanaged);
foreach (var player in SystemAPI.Query<RefRO<LockstepPlayer>>().WithAll<LockstepPlayerJoined>())
{
    var avatar = buffer.CreateEntity();
    buffer.AddComponent(avatar, new Avatar { slot = player.ValueRO.slot });
    buffer.AddComponent(avatar, LockstepTransform.FromPosition(new FixedVector3(player.ValueRO.slot * 2, 0, 0)));
    buffer.AddComponent<LockstepTransformPrevious>(avatar);
}

var leftSlots = new NativeList<int>(Allocator.Temp);
foreach (var player in SystemAPI.Query<RefRO<LockstepPlayer>>().WithAll<LockstepPlayerLeft>())
{
    leftSlots.Add(player.ValueRO.slot);
}
foreach (var (avatar, entity) in SystemAPI.Query<RefRO<Avatar>>().WithEntityAccess())
{
    if (leftSlots.Contains(avatar.ValueRO.slot))
    {
        buffer.DestroyEntity(entity);
    }
}
```

A freed slot can be reused by a later joiner, so per-player data keyed by `slot` must be removed on the leave tick.

## Input and commands

The input is one unmanaged struct (at most 128 bytes) per player per tick; its size is the session's `InputSize`.

```csharp
public struct GameInput
{
    public const byte FIRE = 1;

    public sbyte moveX;     // quantized on the client: -100..100
    public sbyte moveY;
    public byte buttons;
    public byte reserved;   // explicit padding, so no undefined bytes travel
}
```

Client side, in the presentation world (floats are fine here: they are quantized before they leave the machine):

```csharp
[UpdateInGroup(typeof(LockstepInputSystemGroup))]
public partial class GameInputSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<LockstepLocalInput>();
    }

    protected override void OnUpdate()
    {
        var stick = Vector2.ClampMagnitude(ReadStick(), 1f);
        var localInput = SystemAPI.GetSingleton<LockstepLocalInput>();
        localInput.Set(new GameInput
        {
            moveX = (sbyte)Mathf.RoundToInt(stick.x * 100f),
            moveY = (sbyte)Mathf.RoundToInt(stick.y * 100f),
            buttons = FireHeld() ? GameInput.FIRE : (byte)0,
        });
        SystemAPI.SetSingleton(localInput);

        if (BuildClicked(out var cell))
        {
            SystemAPI.GetSingletonBuffer<LockstepCommand>().Add(LockstepCommand.Create(new BuildCommand { cellX = cell.x, cellY = cell.y }));
        }
    }
}
```

Simulation side:

```csharp
foreach (var (player, input, commands) in SystemAPI.Query<RefRO<LockstepPlayer>, RefRO<LockstepPlayerInput>, DynamicBuffer<LockstepCommand>>())
{
    var current = input.ValueRO.Get<GameInput>();
    var previous = input.ValueRO.GetPrevious<GameInput>();
    var firePressed = (current.buttons & GameInput.FIRE) != 0 && (previous.buttons & GameInput.FIRE) == 0;
    var move = new FixedVector2(FixedPoint.FromFraction(current.moveX, 100), FixedPoint.FromFraction(current.moveY, 100));

    for (var i = 0; i < commands.Length; i++)
    {
        if (commands[i].TryGet<BuildCommand>(out var build))
        {
            // Validate here (money, free cell): every client runs the same validation.
        }
    }
}
```

Lists of any length (the unit ids of an order, waypoints) are the command's data, never a `FixedList` capped by the
payload size or a command split in chunks:

```csharp
// Client: the data goes into the LockstepCommandData buffer of the entity the command is added to.
var commands = SystemAPI.GetSingletonBuffer<LockstepCommand>();
var commandData = SystemAPI.GetSingletonBuffer<LockstepCommandData>();
commands.Add(LockstepCommand.Create(new MoveCommand { target = target }, unitIds.AsArray(), commandData));

// Simulation: query the data buffer next to the commands.
foreach (var (player, commands, commandData) in
         SystemAPI.Query<RefRO<LockstepPlayer>, DynamicBuffer<LockstepCommand>, DynamicBuffer<LockstepCommandData>>())
{
    for (var i = 0; i < commands.Length; i++)
    {
        if (commands[i].TryGet<MoveCommand>(out var move))
        {
            var unitIds = commands[i].GetData<uint>(commandData); // view; check each unit's owner before using it
        }
    }
}
```

- The client samples input once per tick. A tap shorter than a tick can be missed: send must-not-miss actions as
  commands or latch them on the client until the next tick.
- Command payload structs are at most 122 bytes; their data has no limit (large commands are fragmented on the wire
  and still arrive in one tick). A player sends at most 32 commands per tick and the rest move to later ticks.
- Command types are identified by a hash of the assembly-qualified type name: renaming the struct, its namespace or
  its assembly changes the id, so all clients need the same build.

## Determinism rules

1. **No `float`, `double`, `Mathf` or `Unity.Mathematics` float math in anything that changes state.** Floating point
   results differ between CPUs, compilers and Burst modes. Use `FixedPoint` and `FixedMath`; build constants from integers and
   fractions (`FixedPoint.FromFraction(7, 2)`), not from float literals computed at runtime.
2. **Time and randomness come from the simulation.** `LockstepTime` (or tick counters), `LockstepRandom` or an
   `FixedRandom` stored in a component. `SystemAPI.Time`, `UnityEngine.Random` and `System.Random` differ per machine.
3. **All state lives in simulation components.** The checksum only sees components, so a divergence in a system
   field goes unnoticed until it corrupts something else; statics are shared by every world of the process (a second
   client in a test, a replay running next to the game); managed objects and components on system entities are not
   hashed at all. Systems must be stateless between ticks (caching an `EntityArchetype` or a query is fine); put
   singleton state on an ordinary entity, not on `state.SystemHandle`.
4. **`Entity` values are never data.** Entities allocates entity ids from one store shared by every world of the
   process, so the same simulated entity has different `Index`/`Version` on every client. Never sort by an `Entity`,
   hash it, seed randomness with it, iterate a hash map keyed by it, or send it in a command. Use `LockstepEntityId`
   (added at creation or baked with `LockstepEntityIdAuthoring`; assigned at the end of the creation tick) and resolve
   it through `LockstepEntityIdMap`. Query iteration order itself is deterministic.
5. **Ordered work only.** No `Interlocked`, no shared accumulators in parallel jobs, no order taken from a
   `NativeList.ParallelWriter`; parallel command buffers use `[ChunkIndexInQuery]` sort keys. Iterating a
   `NativeHashMap`/`Dictionary` gives an order that depends on hashes and history: iterate queries or sorted buffers.
6. **Nothing local.** No branch on the local slot, camera, screen, device, platform or frame rate inside the
   simulation. Local-only behaviour (camera follow, "my unit" highlight) belongs to the presentation.
7. **The simulation world is written only by the lockstep systems.** Presentation code reads it and nothing else.

| Instead of | Use |
|---|---|
| `float`, `double`, `Mathf.*`, `math.sin` | `FixedPoint`, `FixedVector2`, `FixedVector3`, `FixedQuaternion`, `FixedMath.Sin` |
| `SystemAPI.Time.DeltaTime`, `Time.time` | `LockstepTime.deltaTime`, `LockstepTime.tick`, tick counters |
| `UnityEngine.Random`, `System.Random` | `LockstepRandom.value`, an `FixedRandom` in a component |
| `Entity` in commands, sort keys, seeds, hash keys | `LockstepEntityId.value` |
| A `FixedList` of ids in a command payload, orders split into chunks | Command data: `LockstepCommand.Create(payload, data, dataBuffer)`, `GetData<T>` |
| `LocalTransform` for gameplay | `LockstepTransform` |
| Unity Physics queries for gameplay | Own `FixedPoint` collision code |
| NavMesh, `NavMeshAgent` | `LockstepNavAgent` on the `LockstepNavGrid` (`Pragma.Lockstep.Navigation`) |
| `static` counters, system fields with state | Singleton components on ordinary entities |
| Iterating a hash map | Iterate a query or a buffer sorted by a deterministic key |

## Useful patterns

- **Per-slot lookup:** build `new NativeArray<Entity>(info.maxPlayers, Allocator.Temp)` from a query, then index by
  slot. Query order is the same everywhere.
- **Cooldowns:** `int cooldownTicks`, decremented every tick; seconds become ticks with `seconds * time.tickRate`.
- **Distance checks:** compare `FixedMath.DistanceSquared(a, b) < radius * radius` for small coordinates; `LengthSquared` overflows
  above about 46 000 per component, use `FixedMath.Length`/`Distance` for large worlds.
- **Spawning many entities from a query:** collect the spawn data in a `NativeList` (as the sample's `Shot` struct)
  and create them after the loop, or use the end-of-tick command buffer.
- **Per-entity random streams:** `FixedRandom.CreateFromIndex(info.seed, lockstepEntityId.value)` stored in a component,
  created once `LockstepEntityId.IsAssigned` (the tick after creation; on the creation tick every id is still 0).
- **One-shot effects for the presentation:** append `{ tick, kind, position }` to a singleton buffer in the
  simulation and trim old entries; the presentation plays entries newer than the last tick it handled (it may skip
  ticks when it catches up).

## Navigation

Units that walk around obstacles use `Pragma.Lockstep.Navigation` (assembly reference `Pragma.Lockstep.Navigation`):

- **Grid.** One `LockstepNavGrid` per simulation world, baked with `LockstepNavGridAuthoring` into the map subscene
  (a scene entity): size, cell size, agent radius. Its `LockstepNavCell` buffer counts the obstacles over each cell.
- **Ground.** For hilly maps the authoring samples a `TerrainData` (world Y, steepest walkable slope in degrees) into the
  `LockstepNavHeight` buffer: agents walk on the ground (Y is set by navigation, never walked to), cells steeper than
  `LockstepNavGrid.maxSlope` are blocked for good. Put whatever you place on the map on the ground with
  `LockstepNavigation.ToGround(grid, heights, position)` (spawned units, buildings); compare positions on X and Z only,
  since Y now depends on the ground.
- **Obstacles.** `LockstepNavObstacleAuthoring`: on a prefab next to `LockstepTransformAuthoring` the rectangle follows
  the entity (buildings); on a static map object next to `LockstepSceneEntityAuthoring` the pose is baked. Cells are
  blocked while the entity exists; destroying it releases them on the next tick. Keep renderers off scene obstacles.
- **Agents.** `LockstepNavAgentAuthoring` (speed, angular speed in degrees, stopping distance, body radius). Gameplay calls
  `agent.ValueRW.SetDestination(target)` from a system with `[UpdateBefore(typeof(LockstepNavSystemGroup))]` and reads
  `status` (`Requested`, `Moving`, `Arrived`) and `isPathPartial`. Never write `LockstepTransform` of a walking agent
  yourself; call `Stop()` first.
- **Queries.** `LockstepNavigation.IsWalkable`, `HasLineOfSight`, `TryFindNearestWalkable` for AI;
  `IsClear(grid, cells, footprint)` for where a building may be placed (every cell it would block is inside the grid and
  walkable), `GetCoverage` and `Covers` to compare footprints not stamped yet (placements of the same tick);
  `LockstepPathfinder` (scratch memory, one per thread) for paths outside agents.
- **Standing in an obstacle.** An idle or arrived agent whose cell becomes blocked (a building placed on it, a unit
  spawned or stopped inside one) walks to the nearest walkable cell by itself.
- **Keeping apart.** Agents with a `radius` are pushed apart where they overlap (`LockstepNavSeparationSystem`, after
  they walked; a walking agent gives way to a standing one), never onto a blocked cell; paths do not go around other
  agents, and a standing agent may be shouldered a little off its spot. Spread the destinations of a group
  (formation places) rather than sending it to one point. See `references/simulation-api.md` and the README section
  *Navigation*.

## Stats

Numbers that upgrades, research, abilities, auras or cover change (max health, speed, damage, range), and amounts that
gameplay spends and refills (health, morale, energy, money), are stats of `Pragma.Lockstep.Stats` (assembly reference
`Pragma.Lockstep.Stats`), not fields that systems patch by hand:

- **Data.** The game names its stats with an enum (0 = none); the factories and helpers take the enum itself. An entity
  bakes a `LockstepStat` buffer of attributes (`LockstepStat.Attribute(type, baseValue)`) and resources
  (`LockstepStat.Resource(type, amount)`, or `Resource(type, capAttribute, LockstepStatCapPolicy.KeepRatio)`, which
  starts full), plus the empty buffers it takes part in: `LockstepStatModifier`, `LockstepStatChange`,
  `LockstepStatGrantor`, `LockstepStatTarget`.
- **Modifiers (attributes).** `LockstepStatModifier.Flat`, `AdditivePercent` (shares summed), `MultiplicativePercent`
  (times 1 + value, stacking); the percentage factors stop at 0, so -100 % zeroes an attribute and debuffs never flip
  its sign, while `Flat` may push it below 0. Each modifier comes with a `LockstepStatSource` (kind, id), an optional
  `endTick` (`time.tick + durationTicks`) and a `stacking` rule (`Strongest`: one aura of a kind counts once, the
  strongest). Add them in systems with `[UpdateBefore(typeof(LockstepStatSystem))]`; `modifiers.RemoveModifiers(source)`
  ends an effect, and removing before adding again refreshes it instead of stacking.
- **Changes (resources).** Damage, healing, income and payments are `LockstepStatChange.Create(stat, amount, source)`
  added before `LockstepStatSystem`: the tick's changes are summed, the amount stays within 0 and the cap, the buffer is
  cleared. Never write a resource's `value` by hand. Check a price with `stats.TryGetPendingValue(changes, type, out
  amount)`, not `TryGetValue`: `value` does not see the payments already added on this tick.
- **Grants (groups).** Research or a squad ability goes into the `LockstepStatGrant` buffer of the player's or squad's
  entity with a `target` (`ANY` or a `LockstepStatTarget` of the receivers); every entity whose `LockstepStatGrantor`
  names that entity gets it, also the ones created later. Name the grantor where the unit is created.
- **Reading.** `stats.TryGet(StatType.Health, out var health)` (`value`, and `max` for a capped resource) or
  `stats.TryGetValue(type, out value)` in systems with `[UpdateAfter(typeof(LockstepStatSystem))]`. Apply a stat to
  another component (agent speed) in an `IJobEntity` with `[WithChangeFilter(typeof(LockstepStat))]` and
  `in DynamicBuffer<LockstepStat>`: the stat system writes stats only when they change, and a read-write access would
  make it recalculate them every tick. Views show stats with an `EntityBufferView<LockstepStat>` part.
- **Base values.** `stats.TrySetBase` for permanent changes of an attribute (a level up); the value follows in the next
  update.

## Burst and jobs

`FixedPoint` is integer math, so Burst, Mono and IL2CPP produce identical bits; Burst-compile simulation systems freely.
`IJobEntity.ScheduleParallel` is deterministic when each entity writes only itself. Jobs are completed at the end of
every tick. Burst rejects managed arrays: create archetypes with `stackalloc ComponentType[] { ... }`. A failed Burst
compile falls back to managed code with only a console error, so check the console for "Burst error" after changes.

## Presentation

```csharp
if (LockstepWorlds.TryGetClient(presentationWorld, out var client) && client.Simulation != null)
{
    var simulation = client.Simulation;                  // simulation.World: read only
    var lastTick = simulation.Tick - 1;
    var alpha = client.InterpolationAlpha;
    var render = LockstepTransformExtensions.Interpolate(transform, previous, lastTick, alpha); // -> LocalTransform
}
```

- Entities need `LockstepTransformPrevious` to interpolate (add it, or tick *Interpolate* on `LockstepTransformAuthoring`).
- GameObject views (`Pragma.Lockstep.Views`, ported from ECV) are the usual way to show units: give the entity an
  `EntityViewKey` where it is created, make a prefab with `EntityView`, `TransformComponentView` and one part per
  component type (`EntityComponentViewUnmanaged<T>` reacts to changes only), add
  `public partial class XViewUpdateSystem : EntityViewUpdateSystem<X> { }` per type, and bind keys to prefabs in an
  `EntityViewConfig` (baked with `EntityViewConfigAuthoring`, or `EntityViewConfigProvider` when worlds are created on
  demand). Views come from a pool each project can replace (`EntityViewManagerSystem.PoolFactory`). See
  `references/views.md`.
- `LockstepViewSystem` spawns an Entities Graphics copy of the registry prefab for every simulation entity with
  `LockstepPrefabId` (all instances of registry prefabs) and moves it every frame.
- Presentation code never writes to the simulation world, not even a tag. Local state (selection, hover, "is mine":
  `data.slot == View.Client.LocalSlot`) stays in the presentation; changes go through commands.
- Convert for display only: `(float)value`, `(float3)position`, `(quaternion)rotation`, `transform.ToLocalTransform()`.
  Never feed a converted value back into the simulation.

## Before you finish

- [ ] New simulation systems are in `LockstepSimulationSystemGroup`; input readers in `LockstepInputSystemGroup`.
- [ ] No float/double/Mathf/`math.*` float functions, `Time`, `Random`, statics or system-field state in simulation
  code.
- [ ] No `Entity` value used as data; cross-client references use `LockstepEntityId`.
- [ ] No structural change inside a `SystemAPI.Query` loop; parallel command buffers have sort keys.
- [ ] Input changes keep the struct size in sync with `InputSize` (`UnsafeUtility.SizeOf<T>()`).
- [ ] Presentation code (views, parts, UI) only reads the simulation world.
- [ ] The code compiles without Burst errors, and a two-client test or offline run shows no desync.
