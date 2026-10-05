# Pragma DOTS Lockstep

Deterministic lockstep for Unity DOTS (Entities), built on top of Netcode for Entities.

Every client runs the same simulation in its own isolated Entities world. Only player input travels over the network.
A server collects the input of every player, orders it into *confirmed frames* (one per tick) and streams them to all
clients. Each client steps its simulation with exactly the same frames, so every machine computes exactly the same
state. The server never simulates, and the bandwidth depends on the number of players, not on the number of
simulated entities.

## Contents

- [When to use it](#when-to-use-it)
- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [How it works](#how-it-works)
- [Quick start](#quick-start)
- [Writing the simulation](#writing-the-simulation)
- [Determinism rules](#determinism-rules)
- [Fixed-point math](#fixed-point-math)
- [Navigation](#navigation)
- [Stats](#stats)
- [Sessions](#sessions)
- [Presentation](#presentation)
- [Timing and latency](#timing-and-latency)
- [Configuration reference](#configuration-reference)
- [Desync detection and debugging](#desync-detection-and-debugging)
- [Replays](#replays)
- [Testing a simulation](#testing-a-simulation)
- [Custom transports](#custom-transports)
- [Protocol](#protocol)
- [Limits](#limits)
- [Package layout](#package-layout)
- [Claude Code skills](#claude-code-skills)
- [Troubleshooting](#troubleshooting)
- [Not included](#not-included)

## When to use it

Lockstep fits games where many things are simulated but few players send input:

- RTS, tower defense, colony and auto-battler games with hundreds or thousands of units.
- Co-op and competitive games that need exact replays, spectating from input logs, or a provably shared simulation.
- Slow or medium-paced real-time games where about one round trip of input delay is acceptable.

It is a poor fit for:

- Shooters, fighting and racing games where input delay must be hidden. This package is strict lockstep: there is
  no client-side prediction or rollback (yet). Use Netcode for Entities ghosts with prediction for those games.
- Simulations that depend on float-based engine systems (Unity Physics, NavMesh, Animator) for gameplay results.
  Unit movement around obstacles is covered: the package has its own deterministic [navigation](#navigation).

## Features

- **Isolated simulation world.** A plain Entities `World` that changes only one confirmed tick at a time. Gameplay
  systems opt in by living in `LockstepSimulationSystemGroup`; they are never created in the default, client or
  server worlds.
- **Fixed-point math.** `FixedPoint` (Q47.16), `FixedVector2`, `FixedVector3`, `FixedQuaternion` and `FixedMath`
  (shaped after `Mathf`). `Sqrt`, trigonometry, `Exp`, `Log` and `Pow` are integer-only and bit-identical in Mono,
  IL2CPP and Burst. `FixedRandom` is a deterministic PCG32 generator.
- **Input and commands.** The per-tick input is an unmanaged struct of your own; the simulation also gets the
  previous input, so button edges are exact. Commands are discrete actions (build, buy, use) that are never dropped;
  besides their payload struct they carry data of any length (the unit ids of an order).
- **Players as entities.** Joins and leaves travel inside the frames, so they happen on the same tick on every client.
- **Server that never stalls.** When an input is late the server repeats the player's previous input instead of
  waiting, so one slow connection does not slow everybody down. Waiting a bounded number of ticks is opt-in.
- **Adaptive timing.** The server tells each client how early its input arrives and the client steers its input clock
  to land just in time. Confirmed frames go through a jitter-aware playout buffer with a catch-up budget.
- **Desync detection.** Clients hash their whole simulation state; the server compares the hashes by majority and
  tells everybody which client diverged. A per-component breakdown shows what diverged.
- **Navigation.** A walkability grid, obstacles that block it while their entity exists, A* with string pulling in
  integer math and agents that walk the paths in `FixedPoint`, planning again when obstacles change.
- **Stats.** Attributes with flat, additive and multiplicative modifiers (timed or lasting until their source removes
  them, stacking or not) and resources capped by an attribute, changed by one-tick changes such as damage; modifiers a
  player or a squad grants to all of its units, the later ones included. In `FixedPoint`, recalculated only where
  something changed.
- **Late join.** A player joining a running match re-simulates it from tick 0 while new frames keep streaming in.
- **Replays.** Export from a client or the server and play back with checksum verification.
- **Offline mode.** The same protocol over an in-process loopback: single player exercises the complete code path.
- **Netcode for Entities integration.** Connections, handshake and transport come from Netcode; all lockstep traffic
  is one hand-serialized RPC. Works with a listen server (host) or a dedicated server; thin clients send input
  without simulating.
- **Presentation.** GameObject views adapted from ECV (Entity Component View): a pooled prefab per entity key, one
  view part per component type fed when that component changes, transforms interpolated between ticks, and a pool each
  project can replace (com.pragma.pool, Addressables...). `LockstepViewSystem` mirrors entities into Entities Graphics
  copies of the baked registry prefabs, and anything else can read the simulation world directly.
- **Tooling.** Fixed-point inspectors, the *Window > Pragma > Lockstep Sessions* debug window and Claude Code skills.

## Requirements

- Unity 6000.6 (Unity 6.6) or newer.
- Entities 6.6 and Netcode for Entities 6.6 (installed as dependencies).
- Burst is recommended: the per-tick systems of the package (frame application, transform history, entity ids) are
  Burst-compiled, and `FixedPoint` math gives the same bits with or without Burst.

## Installation

In the Package Manager choose **+ > Install package from git URL** and enter:

```
https://github.com/PragmaGame/PragmaDOTSLokstep.git?path=Assets/PragmaDOTSLockstep
```

Or add it to `Packages/manifest.json`:

```json
"com.pragma.dotslockstep": "https://github.com/PragmaGame/PragmaDOTSLokstep.git?path=Assets/PragmaDOTSLockstep"
```

Append `#<tag or commit>` to the URL to pin a revision.

Reference the assemblies you use from your assembly definitions:

| Assembly | Contents | Reference it from |
|---|---|---|
| `Pragma.Lockstep.Mathematics` | `FixedPoint`, vectors, quaternion, `FixedMath`, `FixedRandom`; no Entities dependency | simulation and presentation code |
| `Pragma.Lockstep` | Protocol, simulation world, components, checksums, replays, offline mode, presentation helpers | simulation and presentation code |
| `Pragma.Lockstep.Navigation` | The walkability grid, obstacles, agents, `LockstepPathfinder` and the navigation systems | simulation code |
| `Pragma.Lockstep.Stats` | Attributes, resources, modifiers, changes and grants, `LockstepStatSystem` and the `LockstepStats` helpers | simulation code, and presentation code that shows stats |
| `Pragma.Lockstep.Views` | GameObject views: `EntityView`, view parts, `EntityViewKey`, the view manager, catalogs, the pool abstraction | presentation code, and simulation code that sets an `EntityViewKey` |
| `Pragma.Lockstep.Netcode` | Netcode for Entities RPC and the server and client systems | bootstrap code |
| `Pragma.Lockstep.Authoring` | Bakers for transforms, the prefab registry, scene entities, entity ids, view catalogs and view keys | authoring code, if any |

## How it works

```
 client A                         server                          client B
 ────────                         ──────                          ────────
 input for tick T  ──────────►  collects inputs
                                closes tick T on its clock  ◄────  input for tick T
                                (late input: previous one repeats,
                                 commands move to the next tick)
 frame T  ◄──────────────────── frame T: inputs, commands, ───────►  frame T
                                joins, leaves of every player
 simulate T in the                                                  simulate T in the
 simulation world                                                   simulation world
 hash every N ticks ─────────►  majority vote on hashes  ◄────────  hash every N ticks
```

**Worlds.** A client process has the usual worlds (a Netcode client world, or a local world when offline) plus one
*simulation world* per session:

```
 client or local world (presentation)                 simulation world (deterministic)
 ────────────────────────────────────                 ─────────────────────────────────
 LockstepInputSystemGroup                             LockstepSimulationSystemGroup, once per tick:
   your input systems -> LockstepLocalInput             LockstepFrameApplySystem
 LockstepOfflineSystem or                                LockstepTransformHistorySystem
 LockstepNetcodeClientSystem                             LockstepBeginSimulationEntityCommandBufferSystem
   LockstepClient: sends input, receives frames,         your gameplay systems
   steps the simulation world  ───────────────────►     LockstepEndSimulationEntityCommandBufferSystem
 LockstepViewSystem, EntityViewManagerSystem,            LockstepEntityIdSystem
 GameObject views, the rest of your presentation
   read the simulation world  ◄── read only ───
```

The simulation world is created by `LockstepSimulation` and is never added to the player loop. `LockstepClient` steps
it once for every confirmed frame that is due. Systems reach it through `LockstepSimulationSystemGroup`: the group
carries a custom world filter flag (`LockstepWorldFilter.SIMULATION`), and systems inside it inherit that flag.
System creation order is sorted, so it is the same on every machine.

**Server.** `LockstepServer` closes tick `T` when its clock reaches `T + 1`. The frame of a tick holds the input of
every player whose input changed (otherwise the previous input repeats), the commands, the joins and the leaves.
Frames are tiny and travel over a reliable, ordered channel.

**Client.** `LockstepClient` runs two clocks:

- The *input clock* runs ahead of the server. The server reports how early each input arrived, and the client
  speeds up or slows down so that its input lands `InputMarginTicks` before the deadline.
- The *playout clock* runs behind the newest confirmed frame, by `PlayoutDelayTicks` plus twice the measured jitter,
  and simulates frames as they become due. It catches up within a per-update tick and time budget.

**Transport.** The protocol core (`LockstepServer`, `LockstepClient`) only sees bytes through `ILockstepTransport`.
The Netcode systems move those bytes as RPCs; offline mode and tests use the in-process `LockstepLoopbackNetwork`.

## Quick start

The `Assets/Examples` folder of the repository contains *Lockstep Arena*, a complete playable sample of everything
below.

### 1. Define the input

The input is a plain unmanaged struct, at most 128 bytes. Quantize analog values on the client: the bytes of this
struct are what every client simulates, so the float stick value never reaches the simulation.

```csharp
public struct GameInput
{
    public const byte FIRE = 1;

    // -100..100
    public sbyte moveX;
    public sbyte moveY;
    public byte buttons;
    // Explicit, so no undefined padding byte travels with the input.
    public byte reserved;
}
```

### 2. Write the simulation

Systems in `LockstepSimulationSystemGroup` run once per tick, only in simulation worlds. Keep all state in
components and use `FixedPoint` math.

```csharp
public struct Avatar : IComponentData
{
    public int slot;
}

[UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
[BurstCompile]
public partial struct AvatarSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<LockstepTime>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var time = SystemAPI.GetSingleton<LockstepTime>();
        ref var random = ref SystemAPI.GetSingletonRW<LockstepRandom>().ValueRW.value;

        // A player joined on this tick: give them an avatar. Structural changes are not allowed while a query is
        // iterated, so they go through the command buffer played back at the end of the tick.
        var buffer = SystemAPI.GetSingleton<LockstepEndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);
        foreach (var player in SystemAPI.Query<RefRO<LockstepPlayer>>().WithAll<LockstepPlayerJoined>())
        {
            var avatar = buffer.CreateEntity();
            buffer.AddComponent(avatar, new Avatar { slot = player.ValueRO.slot });
            buffer.AddComponent(avatar, LockstepTransform.FromPosition(new FixedVector3(random.NextFixedPoint(-5, 5), 0, 0)));
            buffer.AddComponent<LockstepTransformPrevious>(avatar);
        }

        // Inputs by slot. Query order is identical on every client.
        var maxPlayers = SystemAPI.GetSingleton<LockstepSessionInfo>().maxPlayers;
        var inputs = new NativeArray<GameInput>(maxPlayers, Allocator.Temp);
        foreach (var (player, input) in SystemAPI.Query<RefRO<LockstepPlayer>, RefRO<LockstepPlayerInput>>())
        {
            inputs[player.ValueRO.slot] = input.ValueRO.Get<GameInput>();
        }

        var speed = FixedPoint.FromFraction(7, 2);
        foreach (var (avatar, transform) in SystemAPI.Query<RefRO<Avatar>, RefRW<LockstepTransform>>())
        {
            var input = inputs[avatar.ValueRO.slot];
            var move = new FixedVector3(FixedPoint.FromFraction(input.moveX, 100), 0, FixedPoint.FromFraction(input.moveY, 100));
            transform.ValueRW.position += move * speed * time.deltaTime;
        }
    }
}
```

A complete game also removes the avatar when the player leaves (`LockstepPlayerLeft`); see
[Players](#players).

### 3. Gather the local input

On the client, write `LockstepLocalInput` every frame from a system in `LockstepInputSystemGroup`. The group exists
only in client and local worlds.

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
        var stick = ReadStick(); // a Vector2 from the Input System, clamped to length 1
        var input = new GameInput
        {
            moveX = (sbyte)Mathf.RoundToInt(stick.x * 100f),
            moveY = (sbyte)Mathf.RoundToInt(stick.y * 100f),
        };

        var localInput = SystemAPI.GetSingleton<LockstepLocalInput>();
        localInput.Set(input);
        SystemAPI.SetSingleton(localInput);
    }
}
```

Outside ECS, `LockstepWorlds.TryGetClient(world, out var client)` followed by `client.SetInput(input)` does the same.

### 4. Start a session

```csharp
// Offline: an in-process server and client inside a client or local world.
world.EntityManager.CreateSingleton(LockstepOfflineConfig.Create<GameInput>(tickRate: 30));

// Host: a Netcode server world and a client world in this process.
var settings = LockstepServerSettings.Default;
settings.TickRate = 30;
settings.MaxPlayers = 4;
settings.InputSize = UnsafeUtility.SizeOf<GameInput>();
settings.MinPlayersToStart = 2;

var server = ClientServerBootstrap.CreateServerWorld("Server");
var client = ClientServerBootstrap.CreateClientWorld("Client");
LockstepNetcode.HostSession(server, settings);
LockstepNetcode.JoinSession(client, LockstepClientSettings.Default);
LockstepNetcode.Listen(server, NetworkEndpoint.AnyIpv4.WithPort(7979));
LockstepNetcode.Connect(client, NetworkEndpoint.LoopbackIpv4.WithPort(7979));

// Join: only a client world.
var remote = ClientServerBootstrap.CreateClientWorld("Client");
LockstepNetcode.JoinSession(remote, LockstepClientSettings.Default);
LockstepNetcode.Connect(remote, NetworkEndpoint.Parse("192.168.1.20", 7979));
```

See [Sessions](#sessions) for bootstraps, dedicated servers, thin clients and the match flow.

### 5. Present it

```csharp
if (LockstepWorlds.TryGetClient(presentationWorld, out var client) && client.Simulation != null)
{
    var simulation = client.Simulation;
    var entityManager = simulation.World.EntityManager; // read only
    var lastTick = simulation.Tick - 1;
    var alpha = client.InterpolationAlpha;
    // For each entity: LockstepTransformExtensions.Interpolate(transform, previous, lastTick, alpha) -> LocalTransform
}
```

Or let the package spawn and move views for you: GameObject prefabs bound to an `EntityViewKey`
([GameObject views](#gameobject-views)), or Entities Graphics copies of the registry prefabs (`LockstepViewSystem`).

## Writing the simulation

### System order

`LockstepSimulationSystemGroup` updates once per confirmed tick:

| Order | System | What it does |
|---|---|---|
| first | `LockstepFrameApplySystem` | Destroys players that left last tick, shifts input to previous, clears commands, disables the joined flag, then applies the frame: new players, inputs, commands, leaves |
| | `LockstepTransformHistorySystem` | Copies `LockstepTransform` into `LockstepTransformPrevious` for interpolation |
| | `LockstepBeginSimulationEntityCommandBufferSystem` | Plays back buffers recorded for the start of the tick |
| | your systems | Ordered with `[UpdateBefore]` / `[UpdateAfter]`, or in your own subgroups |
| last | `LockstepEndSimulationEntityCommandBufferSystem` | Plays back buffers recorded during the tick |
| last | `LockstepEntityIdSystem` | Assigns `LockstepEntityId`s to new entities and rebuilds `LockstepEntityIdMap` |

Systems in the group are created only in simulation worlds. To put a system into a simulation world without the
group, give it `[WorldSystemFilter(LockstepWorldFilter.SIMULATION)]`, or pass it in
`LockstepSimulationOptions.AdditionalSystems`.

### Singletons of the simulation world

| Singleton | Contents |
|---|---|
| `LockstepTime` | `tick` being simulated (from 0), `tickRate`, `deltaTime` (`FixedPoint`, 1 / tick rate), `elapsedTime` (`tick / tickRate`) |
| `LockstepSessionInfo` | `seed`, `tickRate`, `maxPlayers`, `inputSize`, `startData`; `GetStartData<T>()` |
| `LockstepRandom` | `value`, an `FixedRandom` seeded from the session seed |
| `LockstepPlayerSlot` (buffer) | Player entity of every slot, `Entity.Null` when free |
| `LockstepPrefabElement` (buffer) | Prefab registry copied from the presentation world (when there is one) |
| `LockstepEntityIdMap` | Id to entity lookup, rebuilt at the end of every tick |
| `LockstepEntityIdCounter` | Last id handed out |

### Players

A player is an entity created by `LockstepFrameApplySystem` on the tick the player joins:

| Component | Contents |
|---|---|
| `LockstepPlayer` | `slot` in `[0, maxPlayers)`, `joinTick`, `joinData`; `GetJoinData<T>()` |
| `LockstepPlayerInput` | The confirmed input of this tick: `Get<T>()`, `GetPrevious<T>()`, `HasChanged()` |
| `LockstepCommand` (buffer) | Commands confirmed for this tick, cleared every tick |
| `LockstepCommandData` (buffer) | The data of those commands, back to back; read it with `LockstepCommand.GetData<T>` |
| `LockstepPlayerJoined` (enableable) | Enabled only during the join tick |
| `LockstepPlayerLeft` (enableable) | Enabled only during the leave tick; the entity is destroyed at the start of the next tick |

Typical lifecycle code:

```csharp
// Spawn on join (through a command buffer, or collect the slots and create after the loop).
foreach (var player in SystemAPI.Query<RefRO<LockstepPlayer>>().WithAll<LockstepPlayerJoined>())
{
    var character = player.ValueRO.GetJoinData<CharacterChoice>();
    // record the avatar of player.ValueRO.slot
}

// Despawn on leave: collect the slots, then destroy their avatars.
var leftSlots = new NativeList<int>(Allocator.Temp);
foreach (var player in SystemAPI.Query<RefRO<LockstepPlayer>>().WithAll<LockstepPlayerLeft>())
{
    leftSlots.Add(player.ValueRO.slot);
}
```

You can also add your own components to the player entity itself (on the join tick), as long as nothing else must
survive the leave: the entity is destroyed on the tick after `LockstepPlayerLeft`.

A slot becomes free once the leave has been announced in a frame, and a later joiner may get it. Key per-player data
by `slot` only while the player is present.

### Input

`LockstepPlayerInput.Get<T>()` returns the input confirmed for this tick, `GetPrevious<T>()` the one of the previous
tick (zero before the first input), and `HasChanged()` compares the two.

```csharp
var current = input.ValueRO.Get<GameInput>();
var previous = input.ValueRO.GetPrevious<GameInput>();
var firePressed = (current.buttons & GameInput.FIRE) != 0 && (previous.buttons & GameInput.FIRE) == 0;
```

- The input size is part of the session (`InputSize`, `UnsafeUtility.SizeOf<T>()`); every client must send exactly
  that struct.
- The client sends input once per tick. A button pressed and released between two ticks is lost: for presses that
  must never be missed, use a command or latch the button on the client until the next tick.
- When the server repeats a late player's previous input, the simulation sees exactly that, on every client.

### Commands

Commands are discrete actions with a payload struct of up to 122 bytes and, optionally, data of any length. They
travel reliably and are never dropped: when they reach the server late, they move to the next tick.

```csharp
public struct BuildCommand
{
    public int blueprint;
    public int cellX;
    public int cellY;
}

// Client, in LockstepInputSystemGroup:
SystemAPI.GetSingletonBuffer<LockstepCommand>().Add(LockstepCommand.Create(new BuildCommand { blueprint = 2, cellX = 10, cellY = 4 }));
// or, outside ECS: client.AddCommand(new BuildCommand { ... });

// Simulation:
foreach (var (player, commands) in SystemAPI.Query<RefRO<LockstepPlayer>, DynamicBuffer<LockstepCommand>>())
{
    for (var i = 0; i < commands.Length; i++)
    {
        if (commands[i].TryGet<BuildCommand>(out var build))
        {
            // validate and apply for player.ValueRO.slot
        }
    }
}
```

Lists go into the command's data: an array of any unmanaged element type and any length, kept in the
`LockstepCommandData` buffer next to the commands. An order for a whole selection is one command, however many units
it names:

```csharp
public struct MoveCommand
{
    public FixedVector3 target;
}

// Client: the data goes into the LockstepCommandData buffer of the entity the command is added to.
var commands = SystemAPI.GetSingletonBuffer<LockstepCommand>();
var commandData = SystemAPI.GetSingletonBuffer<LockstepCommandData>();
commands.Add(LockstepCommand.Create(new MoveCommand { target = target }, unitIds.AsArray(), commandData));
// or, outside ECS: client.AddCommand(new MoveCommand { target = target }, unitIds.AsArray());

// Simulation:
foreach (var (player, commands, commandData) in
         SystemAPI.Query<RefRO<LockstepPlayer>, DynamicBuffer<LockstepCommand>, DynamicBuffer<LockstepCommandData>>())
{
    for (var i = 0; i < commands.Length; i++)
    {
        if (commands[i].TryGet<MoveCommand>(out var move))
        {
            var unitIds = commands[i].GetData<uint>(commandData);   // a view, valid until the buffer changes
            // validate that each unit belongs to player.ValueRO.slot, then order it
        }
    }
}
```

- A command type is identified by a stable hash of its assembly-qualified name (`LockstepCommand.TypeHashOf<T>()`):
  renaming the struct, its namespace or its assembly changes the id, so all clients need the same build.
- Data has no size limit: a large command is fragmented on the wire and still reaches every client in one tick. It
  takes longer to arrive (Netcode sends what does not fit its reliable window on the following ticks), and on the
  ordered channel the inputs behind it wait for it: fine for an order now and then, not for kilobytes every tick.
- `GetData<T>` ignores trailing bytes that do not fill a whole `T`, the same way on every client; `DataLength` is the
  size in bytes. Data starts at 8-byte boundaries, so any element type reads aligned.
- Each player contributes at most 32 commands to one tick; the rest move to the following ticks, in order.
- Validate commands in the simulation (enough money, valid cell, own units...): every client runs the same validation,
  so a cheating client only desyncs itself.

### Join data and start data

- **Join data** (up to 62 bytes) is sent by a client when joining: `LockstepNetcode.JoinSession(world, settings,
  LockstepBytes.ToFixedList64(new CharacterChoice { id = 3 }))`. The simulation reads it with
  `LockstepPlayer.GetJoinData<T>()`.
- **Start data** (up to 126 bytes) is set on the server (`LockstepServerSettings.StartData`, or
  `LockstepOfflineConfig.startData`) and reaches every client before tick 0. Read it with
  `LockstepSessionInfo.GetStartData<T>()` (map id, game mode, rules).

### Creating and destroying entities

- Main-thread `EntityManager` calls are deterministic: the structural changes happen in system order.
- Command buffers: `SystemAPI.GetSingleton<LockstepEndSimulationEntityCommandBufferSystem.Singleton>()
  .CreateCommandBuffer(state.WorldUnmanaged)` (or the `Begin` variant for the start of the next tick).
- In parallel jobs, record with a parallel writer and use `[ChunkIndexInQuery]` as the sort key, so playback order
  does not depend on thread timing.
- Prefabs: put a `LockstepPrefabRegistryAuthoring` in a subscene of the client (or local) world. The registry is
  copied into every simulation world; instantiate with
  `state.EntityManager.Instantiate(SystemAPI.GetSingletonBuffer<LockstepPrefabElement>(true)[index].prefab)`.
  The registry entity always exists in the simulation world, but it is empty when the presentation world had no
  baked registry when the simulation was created: start the session with `waitForPrefabRegistry`, and check the
  buffer length before indexing.
- Scene entities: what a map starts with (buildings, resource nodes, spawn markers) is placed in that subscene like
  any scene object and gets a `LockstepSceneEntityAuthoring`. The built-in systems copy every `LockstepSceneEntity` of
  the presentation world (with its linked entities) into the simulation before tick 0, sorted by an order baked from
  the object's identity in its scene, so every client starts from the same entities, in archetypes created in the same
  order, whatever order its subscenes loaded in. References between scene entities are kept; references to anything else, registry prefabs included,
  become `Entity.Null`, so a scene entity names a prefab by registry index or by a key of your own. Only GameObject
  views show scene entities: `LockstepViewSystem` mirrors registry instances only, and the presentation world keeps
  its own inert copy of the baked entity.

### Referring to entities

`Entity` values differ between clients: Entities hands out entity ids from one store shared by every world of the
process, so the same simulated entity has a different index and version on every machine. Inside one tick on one
machine an `Entity` is fine as a handle (in a lookup, in a component that points at another entity), but it must never
be *data* that affects the result: never sort by it, hash it, seed randomness with it, or send it in a command.

For identities that must match across clients, use `LockstepEntityId`:

```csharp
// Add it when creating the entity (or bake it with LockstepEntityIdAuthoring).
state.EntityManager.AddComponent<LockstepEntityId>(unit);

// Ids are assigned at the end of the creation tick, starting at 1 and never reused.
// A command carries the id; the simulation resolves it:
var map = SystemAPI.GetSingleton<LockstepEntityIdMap>();
if (map.TryGetEntity(attack.targetId, out var target))
{
    // ...
}
```

### Randomness

```csharp
ref var random = ref SystemAPI.GetSingletonRW<LockstepRandom>().ValueRW.value;
var damage = random.NextInt(10, 20);
var direction = random.NextDirection2();
```

Use `ref`: the generator is a struct, and a copy would not advance the stored state. Draw numbers in a deterministic
order (main thread, query order). For per-entity streams, store a `FixedRandom` in a component, created with
`FixedRandom.CreateFromIndex(sessionSeed, lockstepEntityId.value)` once the id is assigned (`IsAssigned`, from the
tick after creation): on the creation tick every id is still 0, so all streams would start out identical.

### Time

`LockstepTime.deltaTime` is `1 / tickRate` as `FixedPoint` (truncated to the fixed-point step). For durations, prefer
integer tick counters (`cooldownTicks = tickRate / 2`), which are exact; the sample counts cooldowns in ticks.

### Jobs and Burst

- Burst-compiled `ISystem`s and jobs are welcome: `FixedPoint` math is integer math, so Burst produces the same results as
  Mono and IL2CPP.
- `IJobEntity.ScheduleParallel` is deterministic when every entity writes only its own components.
- Avoid anything whose result depends on thread timing: shared accumulators, `Interlocked`, appending to a
  `NativeList.ParallelWriter` and then using the order, parallel hash map writes followed by iteration.
- Every scheduled job is completed at the end of the tick, before the state is hashed or presented.

### What the simulation must not touch

- `SystemAPI.Time`, `UnityEngine.Time`, `DateTime`, `Stopwatch`.
- `UnityEngine.Random`, `System.Random`.
- `float`, `double`, `Mathf`, `Unity.Mathematics` float types for anything that affects the state.
- Static fields, system fields that carry state between ticks, managed components and managed objects.
- Unity Physics, NavMesh and Animator results: write collisions in `FixedPoint` and move units with the package's
  [navigation](#navigation).
- Anything local: the local player, the camera, devices, the frame rate, the platform. The simulation is the same
  everywhere; local effects belong to the presentation.
- Other worlds. And nothing outside the lockstep systems may write to the simulation world.

## Determinism rules

The package makes the plumbing deterministic; the gameplay code has to keep it that way.

1. **No `float` or `double` in simulation logic.** Use `FixedPoint` and `FixedMath`. Convert authoring values once, at bake time
   (`LockstepTransformAuthoring` does it), and quantize input on the client. Unity Physics and `Unity.Mathematics` float math
   are not deterministic across platforms and compilers.
2. **Time and randomness come from the simulation.** `LockstepTime`, `LockstepRandom` or an `FixedRandom` stored in a
   component.
3. **All state lives in components** of the simulation world. Never in system fields, statics or managed objects, and
   never in anything that depends on the local machine.
4. **Never use `Entity` values as data.** Use `LockstepEntityId` for cross-client identity. Query iteration order is
   deterministic (it follows chunk order, which only depends on the simulation's own structural changes); hash map
   iteration order and anything keyed by `Entity` is not.
5. **Order-independent or explicitly ordered parallel work only.** Sort keys for command buffers in jobs; no atomics
   or shared accumulators.
6. **The same build everywhere.** Clients send `LockstepSimulation.DefaultSimulationHash` (protocol version plus the
   list of simulation systems) when joining, and the server refuses a different build while
   `ValidateSimulationHash` is on. Changing `FixedPoint` math or system order also invalidates old replays.

| Instead of | Use |
|---|---|
| `float`, `double`, `Mathf`, `math.sin` | `FixedPoint`, `FixedVector2`, `FixedVector3`, `FixedQuaternion`, `FixedMath.Sin` |
| `SystemAPI.Time.DeltaTime` | `LockstepTime.deltaTime`, or tick counters |
| `UnityEngine.Random`, `System.Random` | `LockstepRandom`, an `FixedRandom` in a component |
| `Entity` in commands, sorting, seeds | `LockstepEntityId` |
| `LocalTransform` in the simulation | `LockstepTransform` |
| Unity Physics queries for gameplay | Your own `FixedPoint` collision code (the sample has circle collisions) |
| `Dictionary` or `NativeHashMap` iteration | Iterate a query or a sorted buffer |
| `if (slot == localSlot)` in a system | Nothing: decide locally in the presentation |

## Fixed-point math

Namespace `Pragma.Lockstep.Mathematics`, assembly `Pragma.Lockstep.Mathematics` (no Entities dependency).

| Type | Role | Float counterpart |
|---|---|---|
| `FixedPoint` | Scalar | `float` |
| `FixedVector2`, `FixedVector3` | Vectors | `float2` / `Vector2`, `float3` / `Vector3` |
| `FixedQuaternion` | Rotation | `quaternion` / `Quaternion` |
| `FixedMath` | Functions and constants | `Mathf`, `math` |
| `FixedRandom` | Random numbers | `Unity.Mathematics.Random` |

### `FixedPoint`

- 64-bit signed raw value with 16 fractional bits (Q47.16): `FixedPoint.FromRaw(65536)` is 1.0.
- Step `1/65536` (about 0.000015), range about ±1.4·10¹⁴.
- Multiplication keeps a 64-bit intermediate: keep the magnitude of a product below about 2·10⁹.
  `FixedMath.MulShiftRound` multiplies with a 128-bit intermediate; division falls back to exact long division when
  the shifted dividend would overflow.

| Creating values | |
|---|---|
| `FixedPoint x = 5;` | Implicit from `int` |
| `FixedPoint.FromFraction(3, 10)` | Exact fraction, truncated toward zero (0.3) |
| `FixedPoint.Parse("0.3")`, `FixedPoint.TryParse` | Deterministic decimal parser (integer arithmetic, culture independent) for config text |
| `FixedPoint.FromRaw(raw)` | From a raw value |
| `(FixedPoint)1.5f`, `(FixedPoint)1.5` | Explicit, rounded to the nearest step. Fine at bake time or for literals; never for values computed with floats during a tick |
| `FixedPoint.Zero`, `One`, `Half`, `Two`, `MinusOne`, `Epsilon`, `MaxValue`, `MinValue` | Constants |

| Operator | Semantics |
|---|---|
| `+`, `-` | Exact (wrap on overflow, like `long`) |
| `a * b` | Rounded to the nearest step (half up) |
| `a * 3`, `3 * a` | Exact product with an integer |
| `a / b`, `a / 3` | Truncated toward zero; division by zero saturates instead of throwing |
| `%` | Remainder with the sign of the dividend, like C#; zero divisor returns zero |
| `(int)x`, `(long)x` | Truncate toward zero, like a float cast |
| `(float)x`, `(double)x` | For presentation only |

Define tuning constants as properties built from integers and fractions, so they are exact:

```csharp
public static class Rules
{
    public static FixedPoint MoveSpeed => FixedPoint.FromFraction(7, 2);
    public static FixedPoint Friction => FixedPoint.FromFraction(9, 10);
}
```

### `FixedMath`

| Group | Functions |
|---|---|
| Basic | `Abs`, `Sign`, `Min`, `Max`, `Clamp`, `Clamp01`, `Lerp`, `InverseLerp`, `Remap`, `Select`, `Step`, `SmoothStep`, `MoveTowards` |
| Rounding | `Floor`, `Ceil`, `Round`, `Truncate`, `Frac`, `FloorToInt`, `CeilToInt`, `RoundToInt` |
| Modulo | `Remainder` (sign of the dividend, like `%`), `Mod` (always in `[0, Abs(y))`), `Wrap(x, min, max)` |
| Powers | `Sqrt`, `Rsqrt`, `Exp`, `Exp2`, `Log`, `Log2`, `Log10`, `Pow(FixedPoint, FixedPoint)`, `Pow(FixedPoint, int)` |
| Trigonometry | `Sin`, `Cos`, `SinCos`, `Tan`, `Asin`, `Acos`, `Atan`, `Atan2` |
| Angles | `ToRadians`, `ToDegrees`, `DeltaAngle` |
| Constants | `Pi`, `TwoPi`, `HalfPi`, `E`, `Ln2`, `Ln10`, `Sqrt2`, `Rad2Deg`, `Deg2Rad`, `Epsilon` |
| `FixedVector2` | `Dot`, `Cross` (z of the 3D cross), `Length`, `LengthSquared`, `Distance`, `DistanceSquared`, `Normalize`, `NormalizeSafe`, `ClampLength`, `MoveTowards`, `Rotate`, `Perpendicular`, `Reflect`, `Direction(angle)`, `Angle(v)`, `Abs`, `Min`, `Max`, `Clamp`, `Lerp`, `Floor`, `Round` |
| `FixedVector3` | `Dot`, `Cross`, `Length`, `LengthSquared`, `Distance`, `DistanceSquared`, `Normalize`, `NormalizeSafe`, `ClampLength`, `MoveTowards`, `Reflect`, `Project`, `ProjectOnPlane`, `Angle(a, b)`, `Abs`, `Min`, `Max`, `Clamp`, `Lerp`, `Floor`, `Round` |
| `FixedQuaternion` | `Mul`, `Rotate`, `Conjugate`, `Inverse`, `Dot`, `Length`, `Normalize`, `NormalizeSafe`, `Nlerp`, `Slerp`, `Angle`, `RotateTowards`, `Forward`, `Up`, `Right` |
| Raw helpers | `MulShiftRound`, `DivideRaw`, `SqrtRounded` |

Accuracy, checked against `double` by the tests: `Sqrt` within 1 step; `Sin`, `Cos`, `Atan`, `Atan2` within 2 steps;
`Asin`, `Acos`, `Log`, `Log2`, `Log10` within 3 steps; `Exp`, `Exp2` and `Tan` within 0.01–0.02 % (or 3 steps).
Everything is computed with integer series and range reduction, without lookup tables, and gives the same bits
everywhere. Out-of-domain inputs saturate instead of producing NaN: `Sqrt` of a negative is 0, `Log` of 0 is
`FixedPoint.MinValue`.

`LengthSquared` overflows for components above about 46 000; `Length` and `Distance` scale internally and do not.

### Vectors and quaternions

- `FixedVector2`, `FixedVector3`: fields `x`, `y`, `z`; component-wise `+ - * /`, scaling by `FixedPoint` and `int`,
  swizzles `Xy`, `Xz`, `Yz`, `Yx`, constants `Zero`, `One`, `Up`, `Down`, `Left`, `Right`, `Forward`, `Back`.
- `FixedQuaternion`: fields `x`, `y`, `z`, `w` (laid out like `Unity.Mathematics.quaternion`), `Identity`, `Xyz`,
  `AxisAngle`, `RotateX/Y/Z`, `Euler` (radians, Unity's order: z, then x, then y), `LookRotation`, `FromBasis`;
  `q * v` rotates a vector, `a * b` applies `b` first.
- Conversions to and from `float2`, `float3`, `quaternion`, `Vector2`, `Vector3` and `Quaternion` are explicit.
  `int2` and `int3` convert implicitly.

### `FixedRandom`

PCG32 (XSH-RR) with 128 bits of state, seeded through SplitMix64.

| Method | Result |
|---|---|
| `new FixedRandom(seed)`, `FixedRandom.CreateFromIndex(seed, index)` | A generator; different indices give independent streams |
| `NextUInt()`, `NextUInt(max)`, `NextUInt(min, max)` | Unbiased integers |
| `NextInt(max)`, `NextInt(min, max)`, `NextBool()` | |
| `NextChance(probability)` | `true` with the given `FixedPoint` probability |
| `NextFixedPoint()`, `NextFixedPoint(min, max)` | Uniform `FixedPoint` in `[0, 1)` or `[min, max)` |
| `NextVector2(min, max)`, `NextVector3(min, max)` | Uniform vectors |
| `NextAngle()`, `NextDirection2()`, `NextDirection3()`, `NextInsideUnitCircle()`, `NextRotation()` | Geometry helpers |

### Inspector

`FixedPoint` fields are edited as decimal numbers, `FixedVector2`/`FixedVector3` as vectors and `FixedQuaternion` as Euler angles in degrees.
The editor converts once and stores raw values.

## Navigation

`Pragma.Lockstep.Navigation` walks units around obstacles inside the simulation: a walkability grid on the XZ plane,
A* in integer math, string pulling, and agents that follow the paths in `FixedPoint`. NavMesh cannot drive gameplay in
lockstep: it works in floats, is built per platform and answers differently on different machines.

| Type | What it is |
|---|---|
| `LockstepNavGrid` | Singleton: origin, cell size, width, height, agent radius and a version that changes with the cells. The cells are the `LockstepNavCell` buffer of the same entity: the number of obstacles over each cell |
| `LockstepNavObstacle` | A rectangle (center, size) in the space of the entity's `LockstepTransform` (yaw and uniform scale apply), or in world space without one. It blocks every cell whose centre is within the agent radius of it |
| `LockstepNavAgent` | Speed, angular speed, stopping distance and body radius of a unit that walks, its destination and status (`Idle`, `Requested`, `Moving`, `Arrived`). The path is its `LockstepNavWaypoint` buffer |
| `LockstepNavSystemGroup` | Inside `LockstepSimulationSystemGroup`: `LockstepNavObstacleSystem` stamps obstacles, `LockstepNavPathSystem` plans and checks paths in parallel, `LockstepNavMoveSystem` walks agents, `LockstepNavSeparationSystem` pushes overlapping agents apart |
| `LockstepPathfinder`, `LockstepNavigation` | The search and the grid queries (`IsWalkable`, `HasLineOfSight`, `TryFindNearestWalkable`, `IsClear`, `Covers`, `GetCoverage`, `Stamp`) for systems of your own |

### Setting it up

1. Add a `LockstepNavGridAuthoring` to the subscene of the map (it brings a `LockstepSceneEntityAuthoring`): the size
   in world units, centred on the GameObject, the cell size and the agent radius. Selecting it previews the cells the
   obstacles of the open scenes block.
2. Give obstacles a `LockstepNavObstacleAuthoring`. On a prefab with a `LockstepTransformAuthoring` the rectangle moves
   and turns with the entity (buildings spawned from the registry). On a static map object, add a
   `LockstepSceneEntityAuthoring`: the pose is baked, non-uniform scale included. Scene entities are copied whole into
   the simulation, so keep renderers off the obstacle's GameObject (a child works).
3. Give units a `LockstepNavAgentAuthoring` (next to a `LockstepTransformAuthoring`), with the radius of their body if
   they should not stand inside each other.
4. Set destinations from a system that updates before `LockstepNavSystemGroup`; the agent plans its path and takes its
   first step on the same tick:

```csharp
[UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
[UpdateBefore(typeof(LockstepNavSystemGroup))]
public partial struct MoveOrderSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (agent, order) in SystemAPI.Query<RefRW<LockstepNavAgent>, RefRO<MoveOrder>>())
        {
            agent.ValueRW.SetDestination(order.ValueRO.target);
        }
    }
}
```

`LockstepNavAgent.Stop()` stops an agent where it stands.

To check where a building may go, build its footprint with `LockstepNavObstacleFootprint.Create(obstacle, transform)`
and ask `LockstepNavigation.IsClear(grid, cells, footprint)`: true when every cell it would block is inside the grid and
walkable. Footprints that are not in the grid yet (two buildings placed on one tick) are compared cell by cell with
`GetCoverage` and `Covers`.

### Behaviour

- **Paths.** A* over the eight neighbours of a cell with integer costs (10 straight, 14 diagonal) and an octile
  heuristic. A diagonal step needs both cells beside it free, so paths never cut a blocked corner. String pulling keeps
  a corner only where the straight line is blocked. Line of sight is traced exactly through the cell borders, and a
  segment through a cell corner counts both cells beside it.
- **Blocked and unreachable destinations.** A destination in a blocked cell moves to the nearest free cell. When the
  destination cannot be reached at all, the path ends at the reachable cell closest to it and `isPathPartial` is set.
  An agent standing in an obstacle first walks to the nearest free cell. An agent standing still on a cell that becomes
  blocked (a building placed on top of it, a unit spawned or stopped inside one) walks to the nearest free cell by
  itself and arrives there.
- **Changing obstacles.** Obstacles block cells while their entity exists. Spawning, moving, resizing or destroying one
  updates the cells on the next navigation update and changes `LockstepNavGrid.version`; walking agents then check the
  rest of their path and plan again when it is blocked. Partial paths are planned again on every change, since the
  destination may have opened up.
- **Walking.** Agents walk at `speed`, keep it through corners, turn to face where they walk (`angularSpeed` radians per
  second; zero turns at once), stop `stoppingDistance` before the end of the path and become `Arrived`. Y follows the
  destination's height. Without a grid agents walk straight to the destination.
- **Keeping apart.** After they walked, agents closer than the sum of their `radius` are pushed apart by half of the
  overlap per tick (an agent at most by its radius), so a crowd that met on one point spreads out over a few ticks
  instead of jumping. Two walking or two standing agents share the push equally; a walking agent takes three quarters
  of it from a standing one, so it slides past units that hold their place and still makes its way to a destination
  one of them stands on. A push never ends on a blocked cell: it is cut to its walkable X or Z part, or dropped. Agents
  with a zero radius take no part. Paths are not planned around other agents: a pushed agent walks on to its next
  waypoint from where it stands, and agents sent to one point crowd around it.
- **Determinism.** Integer and `FixedPoint` math only, a heap with a strict total order, and paths that depend only on
  the grid and their own agent, so planning in parallel gives the same result on any number of threads. Cells count
  obstacles, so the order obstacles are stamped in does not matter. Burst and Mono find bit-identical paths. An
  agent's push is a sum of pair pushes in exact integer math, so the order agents are visited in does not matter
  either; agents on the very same point split along directions picked by their order in the query, which every client
  shares.

### Cost

A search visits each cell at most once and stops at the destination. Its cost grows with the area between start and
destination; an unreachable destination makes it visit the whole reachable area. Paths are planned in a parallel job
with one scratch buffer per chunk, and walking agents only re-check their remaining segments when the grid changes.
Keeping apart sorts the agents with a radius into a spatial hash once per tick and compares each with the agents of the
nine hash cells around it.
Pick the cell size from the narrowest gap units must pass: about the agent radius is a good start.

## Stats

`Pragma.Lockstep.Stats` keeps the numbers of entities in `FixedPoint`: *attributes* that upgrades, research, abilities
and auras change (max health, speed, damage, range) and *resources* that gameplay spends and refills (health, morale,
energy, a player's money). One system applies the modifiers, the grants an entity inherits and the one-tick changes, and
recalculates only what changed.

| Type | What it is |
|---|---|
| `LockstepStat` | Buffer element: one stat of the entity. Its `type` (an id the game defines, usually an enum value; 0 is `NONE`), `kind` (`Attribute` or `Resource`) and `value`; an attribute's `baseValue`; a resource's `cap` (the attribute that caps it), `capPolicy` and `max` (the cap it was fitted to). Made with `LockstepStat.Attribute` and `LockstepStat.Resource` |
| `LockstepStatModifier` | Buffer element: a change to an attribute. Its `stat`, `type` (`Flat`, `AdditivePercent`, `MultiplicativePercent`), `value`, `stacking` (`Stack` or `Strongest`), `source` and `endTick` (`PERMANENT` by default) |
| `LockstepStatChange` | Buffer element: a one-tick change of a resource (`stat`, `amount`, `source`): damage, healing, income, a payment |
| `LockstepStatGrant` | Buffer element of an entity that gives modifiers to the entities inheriting from it: the `modifier` and the `target` a receiver needs (`ANY` for every receiver) |
| `LockstepStatGrantor` | Buffer element: an entity whose grants this one receives (its player, its squad, the unit carrying it) |
| `LockstepStatTarget` | Buffer element: a group the entity belongs to for grants (its type, its category) |
| `LockstepStatSource` | What applied a modifier, a grant or a change: two numbers the game picks (`kind`, `id`) |
| `LockstepStatSystem` | In `LockstepSimulationSystemGroup`: removes expired modifiers and grants, recalculates the attributes whose base values, modifiers or grants changed, fits resources to their caps and applies the tick's changes |
| `LockstepStats` | Extensions on the stat buffer, also taking the game's enum: `TryGet`, `TryGetValue`, `TryGetPendingValue`, `TryGetBase`, `TrySetBase`; `RemoveModifiers`, `RemoveGrants`, `Calculate` (the formula, for tooltips and previews), `Id` |

### Setting it up

Name the stats with an enum of your own and bake the buffers an entity needs: `LockstepStat`, plus
`LockstepStatModifier`, `LockstepStatChange`, `LockstepStatGrantor` and `LockstepStatTarget` for what it takes part in.
Empty buffers cost nothing, and gameplay fills them without structural changes:

```csharp
public enum StatType { None = 0, MaxHealth = 1, Speed = 2, Health = 3 }
public enum StatTarget { Any = 0, Infantry = 1, Vehicle = 2 }

// In a baker:
var stats = AddBuffer<LockstepStat>(entity);
stats.Add(LockstepStat.Attribute(StatType.MaxHealth, (FixedPoint)authoring.maxHealth));
stats.Add(LockstepStat.Attribute(StatType.Speed, (FixedPoint)authoring.speed));
stats.Add(LockstepStat.Resource(StatType.Health, StatType.MaxHealth, LockstepStatCapPolicy.KeepRatio)); // starts full
AddBuffer<LockstepStatModifier>(entity);
AddBuffer<LockstepStatChange>(entity);
AddBuffer<LockstepStatGrantor>(entity);
AddBuffer<LockstepStatTarget>(entity).Add(LockstepStatTarget.Create(StatTarget.Infantry));
```

Change stats in systems that update before `LockstepStatSystem` and read them in systems after it, and both happen on
the same tick:

```csharp
[UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
[UpdateBefore(typeof(LockstepStatSystem))]
public partial struct SprintSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var time = SystemAPI.GetSingleton<LockstepTime>();
        var source = new LockstepStatSource((uint)EffectKind.Sprint, 0);
        foreach (var modifiers in SystemAPI.Query<DynamicBuffer<LockstepStatModifier>>().WithAll<SprintRequest>())
        {
            // Applied again, the effect is refreshed instead of stacking.
            modifiers.RemoveModifiers(source);
            modifiers.Add(LockstepStatModifier.AdditivePercent(StatType.Speed, FixedPoint.Half, source, time.tick + 5 * time.tickRate));
        }
    }
}

// Damage, from a system before LockstepStatSystem: a one-tick change of the target's health.
changes.Add(LockstepStatChange.Create(StatType.Health, -damage, new LockstepStatSource((uint)EffectKind.Shot, attackerId)));

// Research: one grant on the player's entity reaches all of its infantry, also the units made later...
grants.Add(LockstepStatGrant.Create(LockstepStatModifier.AdditivePercent(StatType.MaxHealth, FixedPoint.Half, research), StatTarget.Infantry));
// ...because every unit of the player names that entity where the unit is created.
grantors.Add(new LockstepStatGrantor(playerEntity));

[UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
[UpdateAfter(typeof(LockstepStatSystem))]
[UpdateBefore(typeof(LockstepNavSystemGroup))]
public partial struct SpeedStatSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        new ApplyJob().ScheduleParallel();
    }

    [WithChangeFilter(typeof(LockstepStat))]
    private partial struct ApplyJob : IJobEntity
    {
        // Read with `in`: writing the stats would mark them changed and make LockstepStatSystem recalculate them every tick.
        private void Execute(ref LockstepNavAgent agent, in DynamicBuffer<LockstepStat> stats)
        {
            if (stats.TryGetValue(StatType.Speed, out var speed))
            {
                agent.speed = speed;
            }
        }
    }
}
```

### Behaviour

- **Attributes.** Over the entity's own modifiers and the grants it receives,
  `value = (base + sum of Flat) * max(0, 1 + sum of AdditivePercent) * product of max(0, 1 + MultiplicativePercent)`.
  `AdditivePercent` values are shares that add up (0.25 and 0.15 make +40 %); `MultiplicativePercent` ones stack on each
  other (0.5 and 0.5 make 2.25 times, -0.5 halves). The percentage factors stop at zero: -100 % or less makes the
  attribute zero, and two factors below zero never cancel out into a buff. The flat part may go below zero (negative
  armor or regeneration). A modifier of a resource, or of a stat the entity does not have, changes nothing.
- **Non-stacking modifiers.** Of the `Strongest` modifiers of an attribute with the same type and source `kind`, only
  the one with the largest absolute value applies (the first one on a tie), own modifiers and grants alike: two leaders
  with the same aura give its bonus once.
- **Resources.** The changes of a resource on one tick are summed, then the amount is kept between zero and its cap, so
  the result does not depend on the order the changes were added in. A change of an attribute changes nothing. Until
  `LockstepStatSystem` runs, the buffer lists the tick's changes with their sources, for whatever needs to know who
  dealt the damage.
- **Payments.** `value` is the amount as of the last update, so it does not see the payments already added on this
  tick. Check a price with `stats.TryGetPendingValue(changes, type, out amount)`: the amount the update will leave after
  the changes in the buffer, under the cap as of the last update. Two purchases on one tick then cannot spend the same
  money.
- **Caps.** A resource follows the attribute named in `cap` as that attribute is on the same tick. `KeepRatio` keeps
  the share (80 of 100 becomes 120 of 150, and 80 of 100 again), so a bonus to the maximum neither heals nor wounds;
  `Clamp` keeps the amount and cuts it down to a lower cap. A capped resource starts full, under the modifiers of its cap,
  and so does one whose cap was zero; `max` is the cap it was fitted to, the maximum a bar shows. Without its cap
  attribute a resource is uncapped.
- **Grants.** A grant reaches every entity whose `LockstepStatGrantor` buffer names the granting entity and whose
  `LockstepStatTarget`s include the grant's `target` (any entity, for `ANY`). Adding, removing or expiring a grant
  updates every receiver on the same tick, and an entity that starts inheriting later gets the grants in place. A
  grantor that no longer exists is dropped from the buffer, and with it what it gave. `LockstepStatGrantor` holds an
  `Entity`: a reference inside the simulation world, which the checksum hashes by its target; never sort by it, hash it
  or send it.
- **Timed modifiers and grants.** `endTick` is the tick they are removed on: added on tick T for D ticks, they end on
  T + D and apply from T to T + D - 1. Durations are ticks: `seconds * time.tickRate`.
- **Sources.** `RemoveModifiers` and `RemoveGrants` remove everything of a source and keep the order of the rest.
  Removing a source before adding its modifiers again refreshes an effect instead of stacking it.
- **Base values.** `TrySetBase` changes an attribute's base value (a level up); the value follows in the next stat
  update.
- **Enums.** The factories and the read and write helpers take the game's enum and store its value
  (`LockstepStats.Id`), in Burst too.
- **When values change.** `LockstepStatSystem` decides per chunk, by change versions, whether to recalculate: its stat,
  modifier, grantor and target buffers, the grants in the chunks of its grantors, pending changes and expiring
  modifiers. It writes the stats only then, so systems that follow a stat can use a change filter on `LockstepStat`. A
  recalculation gives the same values however often it runs, so the state never depends on the versions themselves.
- **Determinism.** Integer and `FixedPoint` math only; chunks are processed in parallel and each writes only its own
  entities.

## Sessions

### Offline

```csharp
var config = LockstepOfflineConfig.Create<GameInput>(tickRate: 30);
config.seed = 42;                   // zero picks a random seed
config.checksumInterval = 0;        // nothing to compare against offline
config.waitForPrefabRegistry = true; // when the simulation instantiates registry prefabs
world.EntityManager.CreateSingleton(config);
```

`LockstepOfflineSystem` runs in client and local worlds. It creates an in-process `LockstepServer` and
`LockstepClient` connected through `LockstepLoopbackNetwork`, so offline play runs the whole protocol. Destroy the
singleton to end the session. `LockstepWorlds.TryGetClient(world, ...)` and `TryGetServer(world, ...)` return both
sides.

### Netcode for Entities

`LockstepNetcodeServerSystem` runs in server worlds while a `LockstepServerConfig` singleton exists, and
`LockstepNetcodeClientSystem` runs in client and thin client worlds while a `LockstepClientConfig` singleton exists.
`LockstepNetcode` creates those singletons and wraps the Netcode calls:

| Call | Effect |
|---|---|
| `HostSession(serverWorld, settings)` | Hosts a session (replaces the config if one exists) |
| `JoinSession(clientWorld, settings, joinData, waitForPrefabRegistry)` | Joins as soon as the world is connected |
| `Listen(serverWorld, endpoint)`, `Connect(clientWorld, endpoint)` | `NetworkStreamDriver.Listen` / `Connect` |
| `RequestStart(serverWorld)` | Starts the match on the next server update with whoever joined |
| `RequestEnd(serverWorld, reason)` | Ends the match |
| `TryGetServer(serverWorld, out server)`, `TryGetClient(clientWorld, out client)` | The running session objects |

The same can be done with components: create `LockstepServerConfig { settings }` or
`LockstepClientConfig { settings, joinData, waitForPrefabRegistry }`, create an entity with
`LockstepStartGameRequest` or `LockstepEndGameRequest { reason }`, and read the `LockstepServerStatus` singleton
(`state`, `closedTicks`, `playerCount`) in the server world.

**Bootstrap.** Netcode's automatic bootstrap creates client and server worlds when the game starts (in the editor,
according to the PlayMode Tools). Either use those worlds, or disable the automatic bootstrap with an
`OverrideAutomaticNetcodeBootstrap` component in the scene (or your own `ClientServerBootstrap`) and create worlds on
demand with `ClientServerBootstrap.CreateServerWorld` / `CreateClientWorld`, as the sample does.

**Dedicated server.** Create only a server world (a server build, or `ClientServerBootstrap.CreateServerWorld`),
`HostSession` with `MinPlayersToStart` set to the number of expected players (or call `RequestStart` from your own
lobby logic) and `Listen`. The server never simulates and needs no content, but it compares the clients'
`DefaultSimulationHash` with its own, so build it from the same code as the clients (or turn `ValidateSimulationHash`
off).

**Thin clients.** `LockstepNetcodeClientSystem` also runs in thin client worlds and forces `Simulate` off there: give
them a `LockstepClientConfig` (or call `JoinSession`) and they send input without simulating or reporting checksums.
Handy for load tests with Multiplayer Play Mode.

**Background.** Netcode drops connections of a player that stops updating; set `Application.runInBackground = true`
for hosts and clients.

### Match flow

```
Lobby ──(MinPlayersToStart reached, RequestStart or StartGame)──► Running ──(RequestEnd, EndGame)──► Ended
          countdown of StartDelaySeconds, then tick 0
```

- **Lobby.** Clients that joined wait in `LockstepClientState.Lobby`. Each client creates its simulation world when the
  start message arrives, during the countdown.
- **Running.** The server closes ticks on its clock. `client.StartedEvent` fires when the client enters `Running`.
- **Late join.** With `AllowLateJoin` (default), a client joining a running match receives every frame from tick 0
  and re-simulates them within its catch-up budget (`MaxTicksPerUpdate`, `MaxSimulationMillisecondsPerUpdate`). Its
  player enters the simulation at `client.JoinTick`. `client.SimulatedTicks` against `client.ConfirmedTicks` gives the
  progress for a loading screen. The cost grows with the match length.
- **Leaving.** `client.Leave()`, removing the `LockstepClientConfig`, a dropped connection, or a disposed client
  world: the server announces the leave in a frame and the slot becomes free. A new config joins again over the same
  connection.
- **End.** `RequestEnd` / `server.EndGame(reason)` stops closing ticks and tells everybody;
  `client.EndedEvent` fires and the client finishes simulating the frames it already has.
- **Rejection.** `client.RejectedEvent` reports `LockstepJoinRejectReason`: `InvalidRequest`, `ProtocolMismatch`,
  `SimulationMismatch` (a different build), `SessionFull`, `GameInProgress` (late join is off), `GameEnded`.

### Session objects

| `LockstepClient` | |
|---|---|
| `State` | `Idle`, `Joining`, `Lobby`, `Running`, `Ended`, `Rejected` |
| `LocalSlot`, `JoinTick` | The local player's slot (or -1) and the tick it enters the simulation |
| `Config` | The `LockstepSessionConfig` sent by the server |
| `Simulation` | The `LockstepSimulation` (null before the start and on clients that do not simulate) |
| `ConfirmedTicks`, `SimulatedTicks`, `BufferedTicks` | Frame progress |
| `InterpolationAlpha` | Blend factor for rendering, in `[0, 1]` |
| `RoundTripTime`, `JitterTicks` | Network measurements |
| `IsDesynced`, `DesyncTick`, `LocalChecksums` | Desync state |
| `StartedEvent`, `DesyncedEvent`, `EndedEvent`, `RejectedEvent` | Events |
| `SetInput`, `AddCommand`, `Join`, `Leave`, `ExportReplay` | Actions |

| `LockstepServer` | |
|---|---|
| `State` | `Lobby`, `Running`, `Ended` |
| `Config`, `Settings` | The session configuration and the server settings |
| `ClosedTicks`, `PlayerCount`, `History` | Progress, players present, every closed frame |
| `VerifiedChecksumCount`, `LateInputCount` | Diagnostics |
| `DesyncDetectedEvent`, `ProtocolViolationEvent` | Events |
| `StartGame`, `EndGame`, `ExportReplay`, `TryGetPlayerSlot`, `IsSlotInUse` | Actions and queries |

## Presentation

The simulation world is read-only for everything outside the lockstep systems. Presentation code finds it through
`LockstepWorlds.TryGetClient(world, out var client)` (or `TryGetSimulation`) and reads it after the client update.

### Interpolation

The simulation advances in ticks while the screen renders frames. `client.InterpolationAlpha` is how far the
playout clock is between the previous and the latest simulated tick. Entities with `LockstepTransformPrevious` keep their
start-of-tick transform, and `LockstepTransformExtensions.Interpolate(current, previous, simulation.Tick - 1, alpha)`
returns the blended `LocalTransform`. It returns the current value when the previous transform was not captured on the
latest tick (a newly created entity), so new entities never slide in from the origin.
`simulationManager.TryGetInterpolated(entity, lastTick, alpha, shownSinceTick, out var transform)` reads both
components and applies the rule of the package's views: the current value until the tick after `shownSinceTick` (the
last simulated tick when your view of the entity appeared), because an entity copied from a live one during a tick
carries that entity's capture.

### GameObject views

`Pragma.Lockstep.Views` adapts ECV (Entity Component View) to lockstep: GameObject prefabs show simulation entities,
with one MonoBehaviour *part* per component type that receives the component when it changes. Use it for what Entities
Graphics does not draw well: animators, VFX, health bars, selection rings, world-space UI.

1. **Give entities a key.** `EntityViewKey` (a `FixedString32Bytes`, 1 to 29 bytes of UTF-8) names the view that shows
   an entity. It is simulation data like any other: set it where the entity is created, or bake it on a registry prefab
   with `EntityViewKeyAuthoring`. Changing it swaps the view.

   ```csharp
   var knight = state.EntityManager.CreateEntity(_knightArchetype); // the archetype includes EntityViewKey
   state.EntityManager.SetComponentData(knight, new EntityViewKey("Knight"));
   ```

2. **Make the view prefab.** Put `EntityView` on the root. Add `TransformComponentView` where the view should follow
   the interpolated `LockstepTransform`, and one part per component type to show:

   ```csharp
   public sealed class HealthBarView : EntityComponentViewUnmanaged<Health>
   {
       [SerializeField] private Transform _fill;

       protected override void OnUpdateData(Health data)
       {
           _fill.localScale = new Vector3((float)data.current / (float)data.max, 1f, 1f);
       }
   }
   ```

   `EntityComponentView<T>.UpdateData` runs whenever the chunk holding the entity's `T` was written, so it may repeat
   a value; `EntityComponentViewUnmanaged<T>` compares the bytes, calls `OnUpdateData` only on a change and forgets the
   cached value when a pooled view is bound to another entity. Several parts may show the same type. A part reaches its
   root through `View`: `View.Entity`, `View.Client.LocalSlot`.

   A dynamic buffer (the stats, a production queue) is shown by an `EntityBufferView<T>` part. Its
   `UpdateData(DynamicBuffer<T>)` gets the buffer itself, read-only and valid only during the call; it runs whenever the
   chunk's buffer was written, so compare what the part shows before redrawing:

   ```csharp
   public sealed class HealthBarView : EntityBufferView<LockstepStat>
   {
       [SerializeField] private Transform _fill;

       public override void UpdateData(DynamicBuffer<LockstepStat> stats)
       {
           if (stats.TryGet(StatType.Health, out var health) && health.max > FixedPoint.Zero)
           {
               _fill.localScale = new Vector3((float)health.value / (float)health.max, 1f, 1f);
           }
       }
   }
   ```

3. **Push the component type.** One empty system per type, in any assembly (its presentation filter keeps it out of
   simulation worlds), and one per buffer element type for buffer parts:

   ```csharp
   public partial class HealthViewUpdateSystem : EntityViewUpdateSystem<Health>
   {
   }

   public partial class StatViewUpdateSystem : EntityBufferViewUpdateSystem<LockstepStat>
   {
   }
   ```

   Override `ConfigureQuery` to narrow the simulation query. Transforms need no system of yours:
   `TransformViewUpdateSystem` interpolates them every frame, and a view shows the current transform until one tick
   after it was bound, so it never slides in from where its entity was created.

4. **List the views in a catalog.** Create an `EntityViewConfig` (*Create > Pragma > Lockstep > Entity View Config*)
   and bind each key to a prefab. Bake it with `EntityViewConfigAuthoring` in a subscene loaded by the client (or local)
   world; dedicated server builds bake nothing. Or register it at runtime with `EntityViewConfigProvider` on a scene
   object (`EntityViewConfigs.Add` in code): a subscene loads only into the worlds that exist when it is enabled, a
   runtime catalog reaches presentation worlds created later. Baked catalogs come first; the first binder of a key wins.

`EntityViewManagerSystem` runs early in `PresentationSystemGroup` (`OrderFirst`), before the update systems, and shows
the session of its world (`LockstepWorlds`). When a tick created, destroyed or re-keyed keyed entities, it compares them
with its views: new entities get a view from the pool, entities that went away or changed their key give theirs back.
Views are returned first, so a released instance serves a new entity in the same frame. Then the systems of
`EntityViewUpdateSystemGroup` push the data: only the chunks whose component (or an enableable component of the query)
changed since the previous push, judged by the change versions of the simulation world, and every value to the views
spawned, attached or forced since then, so a view always starts from the current state. Nothing is written to the
simulation world, not even a cleanup component, so views cannot cause desyncs.

| API | Use |
|---|---|
| `EntityViewManager.TryGet(world, out manager)` | The manager of a presentation world |
| `manager.TryGetView(entity, out view)`, `manager.Views` | Spawned views by simulation entity |
| `manager.Attach(entity, view)`, `manager.Detach(view)` | Show an entity on a view you own, such as a HUD panel; it gets the same data and is never pooled |
| `manager.ForceUpdate(entity)` | Push every value of the entity again at the next update |
| `view.Entity`, `view.Client`, `view.IsBound`, `view.IsAttached` | What a view shows |
| `view.IsAutoUpdateEnabled` | Pause the pushes, transform included; turning it back on catches up |
| `view.TryGetData<T>(out data)`, `view.TryGetBuffer<T>(out buffer)` | Read any component or buffer of the entity from the simulation world, such as its `LockstepEntityId` |
| `view.GetComponentView<T>()`, `view.SetComponentViewEnable<T>(bool)`, `view.UpdateData(data)`, `view.UpdateBuffer(buffer)` | Reach or drive the parts by hand |
| `view.RefreshComponentViews()` | Collect the parts again after adding some at runtime |

`Entity` values work as keys only in this process and session. To send a command about the unit under the cursor,
read its id with `view.TryGetData<LockstepEntityId>(out var id)`. State that is local to one player (selection, hover,
fog of war visibility) belongs to the presentation: keep it on the views, never in the simulation.

A pooled instance keeps whatever its parts changed on it: restore that in `BindBreak` (or `Bind`).
`EntityComponentViewUnmanaged<T>` forgets its cached value on `Bind`, and unbinding turns `IsAutoUpdateEnabled` back on.
Parts learn values, not absence: when `T` is removed or disabled the entity leaves the query and the part gets no call,
so show presence with a field (a `bool`) rather than with a tag.

A view goes back to the pool in the frame its entity is destroyed. For a death animation, keep the entity in a dying
state for as many ticks as the animation lasts (a countdown in the simulation, the same on every client), or let a part
leave an effect behind in `BindBreak`. To take the view away from an entity that stays (a unit inside a transport), set
its key to `default`: a key no catalog binds gets no view.

Several presentation worlds can run in one process (the local players of a hot-seat test, a spectator next to a
player), and each one shows its own session. To show one of them, hide the others: `managerSystem.IsShown = false`
returns the views of that world to the pool at once, and a hidden world spawns none; shown again, it spawns the views
of the current state.

#### The view pool

Views come from an `IEntityViewPool` with two methods, `Spawn(prefab)` and `Release(view)`; the manager binds and
unbinds the views, the pool only keeps instances. The default `EntityViewPool` keeps released instances inactive under
one root object per world that survives scene loads (`Prewarm` creates instances ahead). Each project can plug in its
own pool:

- `EntityViewManagerSystem.PoolFactory = world => ...` creates the pool of every presentation world. Set it in the
  game's bootstrap; it is cleared when play mode starts. The manager owns what the factory returns: the views go back
  to it when the world is destroyed, and then it is disposed if it is `IDisposable`.
- `managerSystem.Pool = pool` replaces the pool of one world. The spawned views go back to the previous pool first;
  the caller keeps ownership of the new one.

An adapter for com.pragma.pool, whose pools hold `PrefabPoolObject`s (give each view prefab one next to its
`EntityView`):

```csharp
public sealed class PragmaPoolEntityViewPool : IEntityViewPool
{
    private readonly IPrefabPoolService _pools;

    public PragmaPoolEntityViewPool(IPrefabPoolService pools)
    {
        _pools = pools;
    }

    public EntityView Spawn(EntityView prefab)
    {
        return _pools.Spawn(prefab.GetComponent<PrefabPoolObject>()).GetComponent<EntityView>();
    }

    public void Release(EntityView view)
    {
        _pools.Release(view.GetComponent<PrefabPoolObject>());
    }
}

// In the bootstrap, before the presentation worlds spawn views:
EntityViewManagerSystem.PoolFactory = world => new PragmaPoolEntityViewPool(poolService);
```

### Entities Graphics copies: LockstepViewSystem

`LockstepViewSystem` runs in presentation worlds and mirrors the simulation into rendered entities:

1. Make prefabs with your rendering components, an `LockstepTransformAuthoring` (*Interpolate* on) and the authoring
   components of your simulation data.
2. Put a GameObject with `LockstepPrefabRegistryAuthoring` listing the prefabs into a subscene loaded by the client
   (or local) world. The index in the list is the prefab id; only append to it once a game has shipped.
3. Pass `waitForPrefabRegistry: true` (`JoinSession`, `LockstepOfflineConfig.waitForPrefabRegistry`) so the
   simulation world is created only after the subscene has loaded.
4. In the simulation, instantiate `SystemAPI.GetSingletonBuffer<LockstepPrefabElement>(true)[index].prefab`. The
   copies in the simulation world carry `LockstepPrefabId`.

For every simulation entity with a `LockstepPrefabId`, the view system instantiates the same registry prefab in the
presentation world, adds `LockstepView { simulationEntity }`, writes the interpolated `LocalTransform` every frame and
destroys the view when the simulation entity goes away. `TryGetView(simulationEntity, out view)` finds a view.

### Reading the simulation directly

Anything else (UI, audio, a minimap) can read components straight from the simulation world. The sample's
`ArenaPresentation` builds its scoreboard that way. Cache the queries per simulation world: the world changes when a
new session starts.

### Effects and sounds

Presentation may skip ticks (several ticks can be simulated in one frame) and must never feed anything back. To
trigger one-shot effects, let the simulation record them in its own state with the tick, for example a buffer of
`{ tick, kind, position }` elements trimmed after a second, and let the presentation play the entries newer than the
last tick it handled.

## Timing and latency

What a player sees after pressing a button is roughly:

```
input delay ≈ round trip time
            + (1 + InputMarginTicks + PlayoutDelayTicks + 2 × jitter) / tick rate
            + one rendered frame
```

The `1` is the tick itself: the server closes tick `T` when its clock reaches `T + 1`. `InputMarginTicks` grows
automatically after late inputs, and the playout part is capped by `MaxPlayoutDelayTicks`. At 30 ticks per second and
a 60 ms round trip this is roughly 200 ms, which suits strategy games. Tune it this way:

- **Tick rate.** 15–30 ticks per second is typical for lockstep; 10–20 for RTS. A higher rate shortens the
  quantization delay but costs bandwidth and simulation time. Keep it at or below Netcode's `SimulationTickRate`
  (the server closes ticks inside its update).
- **`InputMarginTicks`** (default 1.5) trades latency for fewer late inputs. Late inputs raise the margin
  automatically and it decays back.
- **`PlayoutDelayTicks`** (default 1) and `MaxPlayoutDelayTicks` (10) trade latency for smoothness on jittery
  connections.
- **`MaxInputWaitTicks`** (default 0) on the server: 0 never waits, so a lagging player's previous input repeats and
  only that player feels the lag. A positive value holds a frame up to that many ticks for missing input: classic
  lockstep, where everybody waits for the slowest player.

Bandwidth: a frame carries a player's record only when something about that player changed, so idle players cost
nothing and an active player costs roughly the input size per tick downstream to every client. Upstream, each client
sends its input once per tick (repeats are a single flag).

## Configuration reference

### `LockstepServerSettings`

Start from `LockstepServerSettings.Default`. The settings are serializable, so they can be edited in an inspector.

| Setting | Default | Meaning |
|---|---|---|
| `TickRate` | 30 | Simulation ticks per second, 1–1000 |
| `MaxPlayers` | 8 | Up to 64 |
| `InputSize` | 0 | `UnsafeUtility.SizeOf<YourInput>()`, at most 128 bytes |
| `Seed` | 0 | Session seed; 0 picks a random one at start |
| `MinPlayersToStart` | 1 | Start automatically when this many players joined; 0 waits for `StartGame` / `RequestStart` |
| `StartDelaySeconds` | 0.5 | Countdown between the start message and tick 0 |
| `AllowLateJoin` | true | Accept players after the start; they re-simulate the match |
| `ChecksumInterval` | 60 | Ticks between state hashes; 0 disables desync detection |
| `MaxInputWaitTicks` | 0 | How long a frame may wait for a missing input; 0 never waits |
| `MaxInputLeadTicks` | 0 (4 s) | Inputs further ahead are ignored (a broken client clock) |
| `FeedbackIntervalTicks` | 4 | Ticks between timing feedback messages to each client |
| `MaxSendBytesPerUpdate` | 32 KB | Frame bytes per connection per update; limits late-join bursts |
| `MaxPacketSize` | 1024 | Largest packet handed to the transport; larger messages are fragmented |
| `ValidateSimulationHash` | true | Refuse clients whose `DefaultSimulationHash` differs |
| `StartData` | empty | Up to 126 bytes, readable in the simulation through `LockstepSessionInfo` |

### `LockstepClientSettings`

Start from `LockstepClientSettings.Default`.

| Setting | Default | Meaning |
|---|---|---|
| `Simulate` | true | Off for bots and thin clients that only send input |
| `InputMarginTicks` | 1.5 | How early input should reach the server |
| `PlayoutDelayTicks` | 1 | Confirmed frames held in reserve; twice the measured jitter is added |
| `MaxPlayoutDelayTicks` | 10 | Upper bound of the playout delay |
| `MaxTicksPerUpdate` | 60 | Catch-up limit per update |
| `MaxSimulationMillisecondsPerUpdate` | 16 | Catch-up time budget per update; at least one tick always runs |
| `PingIntervalSeconds` | 0.5 | Round-trip measurement interval |
| `RecordReplay` | true | Keep every frame so the match can be exported |
| `StopOnDesync` | false | Stop simulating after a reported desync |
| `MaxPacketSize` | 1024 | Largest packet handed to the transport |

### `LockstepOfflineConfig`

`Create<TInput>(tickRate)` fills `tickRate`, `maxPlayers = 1` and `inputSize`. The other fields are `seed`,
`checksumInterval`, `startData`, `joinData` and `waitForPrefabRegistry`.

### `LockstepSimulationOptions`

Used when you create a `LockstepClient`, `LockstepSimulation` or `LockstepReplayPlayer` yourself.

| Option | Default | Meaning |
|---|---|---|
| `WorldName` | "Lockstep Simulation" | Name of the simulation world |
| `AutoDiscoverSystems` | true | Create every system of `LockstepSimulationSystemGroup` |
| `AdditionalSystems` | none | Extra system types, for example `[DisableAutoCreation]` test systems |
| `Initialize` | none | Runs once before tick 0 (for example to copy content in); must do the same on every client |
| `CanCreate` | none | Gate checked before the world is created; frames keep buffering meanwhile |

`LockstepClientWorldUtility.CreateSimulationOptions(presentationWorld, waitForPrefabRegistry)` builds the options the
built-in systems use: they copy the prefab registry and the scene entities of the presentation world into the
simulation world.

## Desync detection and debugging

Every `ChecksumInterval` ticks each simulating client hashes its simulation world (`LockstepChecksum`) and sends the
hash to the server. The server compares the hashes of a tick by majority and broadcasts the result:

- The server raises `DesyncDetectedEvent` with a `LockstepDesyncReport` (`tick`, `slotMask` of the clients that
  disagree; with no majority, for example two players, every reporter is flagged).
- Each client raises `DesyncedEvent(tick, isLocalClientAffected)` and logs an error. With `StopOnDesync` it stops
  simulating.

The hash covers every entity and every unmanaged component of the simulation world. It is independent of padding
bytes, type indices and entity ids (entities are identified by traversal position, and `Entity` fields by the
position of their target). Not hashed: pointers, blob asset references, managed, shared and chunk components,
components on system entities, and anything marked `[LockstepChecksumIgnore]` (a component type or a single field).
Keep simulation singletons on ordinary entities so they are covered.

**Finding the cause.**

1. Note the tick of the report. *Window > Pragma > Lockstep Sessions* lists the running servers and clients with
   their ticks, round trip, jitter and desync state, and exports replays.
2. Find the component that diverged. All clients received the same frames, so re-simulate the same replay to the
   reported tick on each machine and compare `LockstepChecksum.ComputePerType` (a hash per component type):

   ```csharp
   using (var replay = LockstepReplay.Read(bytes))
   using (var simulation = new LockstepSimulation(replay.Config))
   {
       for (var tick = replay.Frames.FirstTick; tick <= desyncTick; tick++)
       {
           replay.Frames.TryGet(tick, out var frame, out var length); // unsafe: frame is a byte*
           simulation.Step(frame, length);
       }
       foreach (var pair in LockstepChecksum.ComputePerType(simulation.World.EntityManager))
       {
           Debug.Log($"{pair.Key}: {pair.Value:X16}");
       }
   }
   ```

   *Log state hashes* in the debug window prints the same list for a running client. When the simulation
   instantiates registry prefabs, pass the options the game uses
   (`LockstepClientWorldUtility.CreateSimulationOptions(presentationWorld, false)`) so the registry is copied in.
3. Look for the usual causes in the systems that write that component: float math, `Entity` values used as data,
   hash map iteration, parallel writes, state outside components, local-only branches.
4. Reproduce it in a test: two clients in one process already diverge when the bug depends on `Entity` values or on
   static state (see [Testing a simulation](#testing-a-simulation)). `LockstepReplayPlayer.SimulateToEnd()` returns
   the first tick that no longer matches the recorded checksums.

## Replays

A replay is the session configuration, every confirmed frame and the checksums recorded while playing. Clients record
by default (`RecordReplay`); the server always keeps the frames.

```csharp
File.WriteAllBytes(path, client.ExportReplay()); // or server.ExportReplay(), or the debug window

using (var replay = LockstepReplay.Read(File.ReadAllBytes(path)))
using (var player = new LockstepReplayPlayer(replay))
{
    // Whole match at once, verifying the recorded checksums: -1 when everything matched.
    var firstMismatch = player.SimulateToEnd();
}
```

For watching a replay, call `player.Update(deltaTime)` every frame (`Speed` scales playback) and present
`player.Simulation.World` with `player.InterpolationAlpha`. When the simulation instantiates registry prefabs, create
the player with the options the game uses: `new LockstepReplayPlayer(replay,
LockstepClientWorldUtility.CreateSimulationOptions(presentationWorld, false))`.

A replay plays back only with the same build of the simulation: the same systems in the same order, and the same
math.

## Testing a simulation

Determinism bugs are cheapest to find in EditMode tests that run whole sessions in one process. A server and several
clients talk through `LockstepLoopbackNetwork`, which can add latency and jitter, and the test drives the time:

```csharp
[Test]
public void TwoClients_StayInSync()
{
    var settings = LockstepServerSettings.Default;
    settings.InputSize = UnsafeUtility.SizeOf<GameInput>();
    settings.MinPlayersToStart = 2;
    settings.ChecksumInterval = 10;

    var network = new LockstepLoopbackNetwork { Latency = 0.05, Jitter = 0.03 };
    using (var server = new LockstepServer(settings, network.ServerTransport))
    using (var a = new LockstepClient(LockstepClientSettings.Default, network.CreateClientTransport(1)))
    using (var b = new LockstepClient(LockstepClientSettings.Default, network.CreateClientTransport(2)))
    {
        network.AttachServer(server);
        network.AttachClient(1, a);
        network.AttachClient(2, b);
        var desyncs = 0;
        server.DesyncDetectedEvent += _ => desyncs++;
        a.Join();
        b.Join();

        for (var time = 0.0; time < 10.0; time += 0.016)
        {
            a.SetInput(new GameInput { moveX = 100 });
            b.SetInput(new GameInput { moveY = -50 });
            network.Deliver(time);
            a.Update(time);
            b.Update(time);
            network.Deliver(time);
            server.Update(time);
            network.Deliver(time);
        }

        Assert.AreEqual(0, desyncs);
        Assert.Greater(server.VerifiedChecksumCount, 0);
    }
}
```

- Pass `new LockstepSimulationOptions { AutoDiscoverSystems = false, AdditionalSystems = new[] { typeof(MySystem) } }`
  to the clients to test a few systems in isolation. A simulation that instantiates registry prefabs needs a registry
  in tests too: create one in `LockstepSimulationOptions.Initialize`.
- Compare `a.LocalChecksums` with `b.LocalChecksums` for a per-tick assertion.
- Keep a replay of a real match as a test asset and assert that `SimulateToEnd()` returns -1: it fails as soon as a
  change alters the simulation, which also tells you that old replays stopped working.

## Custom transports

`LockstepServer` and `LockstepClient` work over any reliable, ordered transport (Steam networking, a relay, WebSockets).
Implement `ILockstepTransport.Send(connectionId, data, length)` and feed the received bytes back:

```csharp
// Server side
var server = new LockstepServer(settings, myServerTransport);
server.OnConnected(connectionId);                     // a peer connected
server.OnPacket(connectionId, data, length, now);    // bytes from that peer
server.OnDisconnected(connectionId);                  // the peer left
server.Update(now);                                   // every frame

// Client side
var client = new LockstepClient(clientSettings, myClientTransport, simulationOptions);
client.Join(joinData);                                // once connected
client.OnPacket(data, length, now);                  // bytes from the server
client.Update(now);                                   // every frame
```

- `now` is a monotonic time in seconds; every call of one side must use the same clock.
- Packets never exceed `MaxPacketSize`; larger messages are fragmented and reassembled by the package.
- The transport must deliver every packet of a connection exactly once and in order (a reliable, ordered channel).
- Register the client with `LockstepWorlds.RegisterClient(presentationWorld, client)` so `LockstepViewSystem` and the
  debug window find it, and unregister it when done.

## Protocol

All messages are little-endian byte strings, versioned by `LockstepProtocol.VERSION`.

| Message | Direction | Contents |
|---|---|---|
| `JoinRequest` | client → server | Protocol version, simulation hash, flags (simulates), join data |
| `JoinAccepted` / `JoinRejected` | server → client | Slot / reason |
| `Start` | server → client | Session configuration, the client's slot and join tick, the server clock position |
| `Input` | client → server | Inputs of consecutive ticks; a repeat flag instead of unchanged bytes; commands |
| `Frames` | server → client | Consecutive confirmed frames |
| `InputFeedback` | server → client | How early the client's inputs arrive (1/256 tick), late drops |
| `Checksum` | client → server | Tick and state hash |
| `Desync` | server → client | Tick and slot mask |
| `Ping` / `Pong` | client → server / server → client | Round-trip measurement |
| `Leave` | client → server | The player leaves |
| `End` | server → client | End of the match and its reason |

A frame is a record count followed by one record per player with news: the slot, flags (`Joined`, `Left`, `Input`,
`Commands`) and the matching payloads. A player without a record keeps the previous input. A command is written the
same way in `Input` and in frames: type hash, payload size, payload, data length (var uint), data. The server checks
that form and relays the bytes without decoding them.

## Limits

| Limit | Value |
|---|---|
| Players | 64 |
| Input size | 128 bytes per tick |
| Command payload | 122 bytes, plus data of any length |
| Commands per player per tick | 32; extra ones move to the next ticks |
| Join data / start data | 62 / 126 bytes |
| View key (`EntityViewKey`) | 29 bytes of UTF-8 |
| `FixedPoint` | step 1/65536, range ±1.4·10¹⁴, products below about 2·10⁹ |

## Package layout

| Path | Contents |
|---|---|
| `Runtime/Mathematics` | `Pragma.Lockstep.Mathematics`: `FixedPoint`, `FixedVector2`, `FixedVector3`, `FixedQuaternion`, `FixedMath`, `FixedRandom` |
| `Runtime/Core` | Protocol, `LockstepServer`, `LockstepClient`, settings, framing, frame history, replays, loopback network |
| `Runtime/Simulation` | `LockstepSimulation`, the system group, command buffer systems, frame application, checksums, entity ids |
| `Runtime/Simulation/Components` | The simulation components and singletons (`LockstepTime`, `LockstepPlayer`, `LockstepCommand`...) |
| `Runtime/Transforms` | `LockstepTransform`, `LockstepTransformPrevious`, transform history |
| `Runtime/Client` | Local input, `LockstepWorlds`, offline mode, prefab registry copy, `LockstepViewSystem` |
| `Runtime/Navigation` | `Pragma.Lockstep.Navigation`: the grid, obstacles, agents, `LockstepPathfinder`, the navigation systems |
| `Runtime/Stats` | `Pragma.Lockstep.Stats`: attributes and resources, modifiers, changes and grants, `LockstepStatSystem`, `LockstepStats` |
| `Runtime/Views` | `Pragma.Lockstep.Views`: GameObject views (`EntityView`, view parts, `EntityViewManagerSystem`, update systems, catalogs, the pool) |
| `Runtime/Netcode` | `Pragma.Lockstep.Netcode`: the RPC, server and client systems, `LockstepNetcode` |
| `Runtime/Netcode/Components` | `LockstepServerConfig`, `LockstepClientConfig`, `LockstepServerStatus`, start and end requests |
| `Runtime/Authoring` | `Pragma.Lockstep.Authoring`: bakers (transforms, prefab registry, scene entities, entity ids, view catalogs and keys, navigation grid, obstacles and agents) |
| `Editor` | `Pragma.Lockstep.Editor`: fixed-point drawers, the debug window |
| `Skills~` | Claude Code skills (Unity skips folders whose name ends with `~`) |

## Claude Code skills

The package ships [Claude Code](https://docs.claude.com/en/docs/claude-code/overview) skills that teach an agent
how to work with it:

| Skill | Use it for |
|---|---|
| `pragma-lockstep-gameplay` | Writing simulation code: systems, input, commands, players, `FixedPoint` math, spawning, navigation, stats; presentation and GameObject views |
| `pragma-lockstep-sessions` | Offline, host, join, dedicated servers, settings, match flow, custom transports, replays |
| `pragma-lockstep-desync` | Finding and preventing desyncs, determinism tests |

The skills live in the package's `Skills~` folder. Claude Code loads project skills from `.claude/skills` in the
project root, so copy them there once after installing (and again after updating the package):

```bash
# bash, from the project root
mkdir -p .claude/skills
cp -r Library/PackageCache/com.pragma.dotslockstep@*/Skills~/* .claude/skills/
```

```powershell
# PowerShell, from the project root
New-Item -ItemType Directory -Force .claude/skills | Out-Null
Copy-Item -Recurse -Force (Resolve-Path "Library/PackageCache/com.pragma.dotslockstep@*/Skills~/*") .claude/skills/
```

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `LockstepNetcode.TryGetClient` finds no client | The client world is not connected yet (the session starts once Netcode assigned a `NetworkId`): wrong address or port, or the server is not listening |
| The client stays in `Joining` | No `LockstepServerConfig` in the server world: the server drops the packets |
| Rejected with `SimulationMismatch` | The client and the server builds have different simulation systems |
| Rejected with `GameInProgress` | The match runs and `AllowLateJoin` is off |
| "The input set on the client is N bytes but the session input size is M bytes" | `InputSize` does not match the struct written with `LockstepLocalInput.Set` |
| The match never starts | `MinPlayersToStart` is higher than the number of players; call `RequestStart` |
| Nothing moves although the session runs | The gameplay systems are not in `LockstepSimulationSystemGroup`, or no system writes `LockstepLocalInput` |
| Entities Graphics copies do not appear | No `LockstepPrefabRegistryAuthoring` in the presentation world, the entity was not instantiated from the registry, or the simulation started before the subscene loaded (`waitForPrefabRegistry`) |
| GameObject views do not appear | The entity has no `EntityViewKey`, no catalog binds its key (keys are case-sensitive), or the catalog was baked into a subscene that did not load into this world: register it with `EntityViewConfigProvider` |
| A map object is missing from the simulation | It has no `LockstepSceneEntityAuthoring`, it sits in another subscene than the registry, or the session started without `waitForPrefabRegistry` |
| A GameObject view misses a component | No `EntityViewUpdateSystem<T>` subclass for that type, or the part sits under a nested `EntityView` |
| A GameObject view does not move | No `TransformComponentView` on it, or the entity has no `LockstepTransform`; without `LockstepTransformPrevious` it moves in steps |
| Agents do not move | The agent has no `LockstepTransform` or `LockstepNavWaypoint` buffer, its speed is zero, or the destination was set after `LockstepNavSystemGroup` updated (it starts on the next tick) |
| Agents walk through an obstacle | The obstacle is not in the simulation world (a static map object needs `LockstepSceneEntityAuthoring`), lies outside the grid, or is thinner than a cell with an agent radius of zero |
| Agents stop short of the destination | `isPathPartial`: the destination is blocked or walled in, and the path ends at the closest reachable point |
| A stat keeps its old value | The modifier names another stat or a resource (resources take `LockstepStatChange`s), the change names an attribute, the receiver lacks the grant's `LockstepStatTarget` or a `LockstepStatGrantor` naming the granting entity, or it was added after `LockstepStatSystem` updated (it applies from the next tick) |
| A capped resource stays empty or ignores its cap | The entity has no attribute of the `cap` type, so the resource is uncapped; bake the cap attribute in the same buffer |
| `LockstepStatSystem` recalculates every tick | A system writes the stats every tick, also by only taking `DynamicBuffer<LockstepStat>` in a `SystemAPI.Query`, which is read-write: read them with `in` in an `IJobEntity` or with a read-only `BufferLookup` |
| Connections drop when the window loses focus | `Application.runInBackground` is off |
| A "Burst error" in the console | Burst falls back to managed code silently in some cases; fix the reported construct (for example a managed array in a Burst method) |
| Desync reports | See [Desync detection and debugging](#desync-detection-and-debugging) |

## Not included

- **Prediction and rollback.** This is strict lockstep. The isolated world and the state hash are a base for a
  predicted world later.
- **Late join from a snapshot.** Joining re-simulates the match from tick 0, which grows with the match length.
- **Deterministic physics.** Use `FixedPoint` math for collisions (the sample does). Unity Physics is not deterministic
  across platforms.
- **Local avoidance and richer navigation.** Agents follow their own paths and may overlap. One grid per world with one
  agent radius, flat (no heights or cell costs), and no flow fields for large groups.

## License

MIT, see [LICENSE.md](LICENSE.md).
