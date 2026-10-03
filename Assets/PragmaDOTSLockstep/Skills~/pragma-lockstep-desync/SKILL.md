---
name: pragma-lockstep-desync
description: Diagnose, fix and prevent desyncs in Pragma DOTS Lockstep (package com.pragma.dotslockstep) simulations - interpret "[Lockstep] Desync detected" errors and LockstepDesyncReport, compare per-component state hashes (LockstepChecksum.ComputePerType), re-simulate replays to the diverging tick, hunt nondeterministic simulation code (float math, Entity values used as data, hash map order, parallel writes, static or system-field state, local-only branches, mismatched builds) and write determinism tests with LockstepLoopbackNetwork. Use it whenever clients disagree, checksums or replays differ, a match plays out differently on two machines, or new simulation code must be proven deterministic - even if the user only says "the game diverges" or "units are in different places for my friend".
---

# Desyncs in Pragma DOTS Lockstep

Every client steps its simulation with the same confirmed frames: the server orders them and they travel over a
reliable, ordered channel. So when two clients disagree, the frames are not the problem. A desync is always one of:

1. simulation code that depends on something other than the simulation state and the frames, or
2. clients running different simulation code (different builds or content).

Do not look for "lost packets" or "network lag" as the cause; look for nondeterminism.

Related skills: `pragma-lockstep-gameplay` (how to write simulation code correctly), `pragma-lockstep-sessions`
(settings, replays, debug window). `references/tests.md` holds determinism test templates.

## How detection works

- Every `ChecksumInterval` ticks (server setting, default 60) each simulating client hashes its simulation world with
  `LockstepChecksum.Compute` and sends it. The server compares the hashes of a tick by majority.
- On disagreement the server raises `DesyncDetectedEvent(LockstepDesyncReport { tick, slotMask })`; every client
  raises `DesyncedEvent(tick, isLocalClientAffected)` and logs
  `[Lockstep] Desync detected at tick T (slots mask 0x..), including this client.`. With two players there is no
  majority, so both are flagged. `StopOnDesync` stops the simulation.
- The hash covers every entity (by traversal position) and every unmanaged component field, including enabled bits.
  `Entity` fields are hashed as the position of their target, so entity ids never matter. Not hashed: padding,
  pointers, blob asset references, managed / shared / chunk components, components on system entities,
  `[LockstepChecksumIgnore]` types and fields.
- A report at tick T means the states differed at T and matched at the previous checksum tick. The root cause lies
  in between, or in unhashed data (a static, a system field, a managed object) that diverged earlier and leaked into
  components now.

## Triage workflow

1. **Collect.** The tick, which clients were flagged, the builds they run, and a replay (`client.ExportReplay()`, the
   debug window, or `server.ExportReplay()`).
2. **Same build?** `ValidateSimulationHash` only compares the *list* of simulation systems (and the protocol version).
   A changed system body, a different asset or a different config file passes that check and desyncs. Make sure both
   machines run the same build and the same content.
3. **Narrow the window.** Reproduce with a small `ChecksumInterval` (1-10) so the first bad tick is close to the cause.
4. **Find the component.** On each machine, re-simulate the same replay to the reported tick and compare the
   per-type hashes; the type whose hash differs is the one that diverged:

   ```csharp
   using (var replay = LockstepReplay.Read(bytes))
   using (var simulation = new LockstepSimulation(replay.Config, options)) // options: as the game creates them
   {
       for (var tick = replay.Frames.FirstTick; tick <= desyncTick; tick++)
       {
           replay.Frames.TryGet(tick, out var frame, out var length); // unsafe context: frame is a byte*
           simulation.Step(frame, length);
       }
       foreach (var pair in LockstepChecksum.ComputePerType(simulation.World.EntityManager).OrderBy(p => p.Key))
       {
           Debug.Log($"{pair.Value:X16}  {pair.Key}");
       }
   }
   ```

   For a running client, *Window > Pragma > Lockstep Sessions > Log state hashes* prints the same list (with the
   tick). `options` must copy the prefab registry when the game uses it:
   `LockstepClientWorldUtility.CreateSimulationOptions(presentationWorld, false)`.
5. **Find the writer.** Search the systems that write the diverging component (and anything they read) for the
   causes below. If the component is derived from another one, follow the data back.
6. **Reproduce in one process.** Two clients in an EditMode test (`references/tests.md`) already diverge for bugs
   that depend on `Entity` values (the worlds of one process get different entity ids) or on static state shared
   between worlds. Bugs that only show up across machines (float differences between CPUs) need the replay
   comparison from step 4.
7. **Fix and lock it in.** Fix the cause, then keep the reproduction as a test.

## Causes and fixes

| Pattern in simulation code | Why it diverges | Fix |
|---|---|---|
| `float`, `double`, `Mathf`, `math.sin`, `Vector3`, float `Unity.Mathematics` types | Rounding differs between CPUs, compilers, Burst modes | `FixedPoint`, `FixedMath`, `FixedVector2`/`FixedVector3`/`FixedQuaternion` |
| `(FixedPoint)someFloatComputedAtRuntime` | The float is already different | Compute in `FixedPoint`; convert floats only at bake time or for literals |
| Sorting by `Entity`, `entity.Index` as a seed or id, `Entity` in commands | Entity ids come from one process-wide store and differ per client | `LockstepEntityId`, slots, or query order |
| Iterating `NativeHashMap`, `NativeParallelHashMap`, `Dictionary`, `HashSet` | Order depends on hashes, capacity and insertion history (and on `Entity` keys) | Iterate a query or a buffer sorted by a deterministic key |
| `NativeList.ParallelWriter`, `NativeQueue` filled in parallel and then iterated | Order depends on thread timing | Write per entity, or sort the results by a deterministic key before use |
| `Interlocked`, atomics, shared accumulators in parallel jobs | Order (and float sums) depend on timing | Per-entity writes, or a single-threaded reduction in a fixed order |
| Parallel `EntityCommandBuffer` without a proper sort key | Playback order depends on threads | `[ChunkIndexInQuery]` (or another deterministic key) as the sort key |
| Unstable sort with equal keys | Ties may come out in a different order after other changes | Add a tie-breaker (`LockstepEntityId`, slot) |
| `static` fields, system fields carrying state, managed singletons, caches, components on a system entity | Shared by all worlds of the process or not hashed | Components and singletons on ordinary entities of the simulation world |
| `SystemAPI.Time`, `UnityEngine.Time`, `DateTime`, `Stopwatch` | Local clocks differ | `LockstepTime`, tick counters |
| `UnityEngine.Random`, `System.Random`, `Unity.Mathematics.Random` seeded locally | Different seeds and sequences | `LockstepRandom` (by `ref`) or an `FixedRandom` in a component |
| `if (slot == localSlot)`, camera, screen, platform, quality checks | Different on every machine | Move to the presentation |
| Presentation or UI writing into the simulation world | Only one client changes | Send a command |
| Reading other worlds, GameObjects, Unity Physics, NavMesh, Animator from the simulation | Local, float-based state | Only simulation data and `FixedPoint` code; `Pragma.Lockstep.Navigation` for paths |
| Runtime-loaded config (ScriptableObject edited locally, JSON, PlayerPrefs) | Different values per machine | Bake it, or send it as start data |
| `#if UNITY_EDITOR` / platform defines in simulation code | Different code per platform | Same code everywhere |
| `float.Parse`, `ToString` round trips | Culture and float formatting | `FixedPoint.Parse` |
| Uninitialized memory (`NativeArrayOptions.UninitializedMemory`, `stackalloc` read before write) | Garbage differs | Clear before use |
| An exception in a system on one client | The rest of that system's update is skipped there | Fix the exception; check the console of every client |

## Tools

| Tool | Use |
|---|---|
| `LockstepServer.DesyncDetectedEvent`, `LockstepClient.DesyncedEvent`, `IsDesynced`, `DesyncTick` | Detection |
| `client.LocalChecksums` | Every hash this client computed, by tick |
| `LockstepChecksum.Compute(entityManager)`, `ComputePerType(entityManager)` | Whole-world and per-component hashes |
| *Window > Pragma > Lockstep Sessions* | Session state, *Export replay*, *Log state hashes* |
| `LockstepReplay`, `LockstepReplayPlayer.SimulateToEnd()` | Re-simulate a match; first tick that differs from the recorded checksums (a client's own hashes, or the majority hashes in a server export) |
| `LockstepSimulation` + `replay.Frames.TryGet` | Step a replay tick by tick and inspect the world |
| `[LockstepChecksumIgnore]` | Exclude debug-only data from the hash (never to hide a real divergence) |

## Prevention

- Run a two- or three-client EditMode session with checksums every few ticks for every new mechanic
  (`references/tests.md`), with latency, jitter and uneven frame times.
- Keep a golden replay test: a recorded match must still replay without mismatches, or the change knowingly breaks
  replays.
- Review simulation code against the table above before finishing; the `pragma-lockstep-gameplay` checklist covers
  the same rules from the writing side.
