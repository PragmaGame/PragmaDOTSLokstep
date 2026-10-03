# Determinism test templates

EditMode tests (NUnit, Unity Test Framework). The test assembly references `Pragma.Lockstep`,
`Pragma.Lockstep.Mathematics`, `Unity.Entities`, `Unity.Collections` and your game assembly, and allows unsafe code
for the replay templates. Simulations created in tests auto-discover every system of
`LockstepSimulationSystemGroup`, so the whole game simulation runs.

## Contents

- Several clients over a loopback network
- Two simulations from the same frames
- Golden replay
- Isolating a few systems

## Several clients over a loopback network

Runs the real protocol: a server, three clients, latency, jitter, uneven frame times, different input per player.
Any nondeterminism that shows up inside one process (Entity values used as data, static state, order bugs) produces
a desync report.

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Pragma.Lockstep;
using Unity.Collections.LowLevel.Unsafe;

public class DeterminismTests
{
    [Test]
    public void Clients_StayInSync_UnderLatencyAndJitter()
    {
        var settings = LockstepServerSettings.Default;
        settings.TickRate = 30;
        settings.MaxPlayers = 4;
        settings.InputSize = UnsafeUtility.SizeOf<GameInput>();
        settings.Seed = 1234;
        settings.MinPlayersToStart = 3;
        settings.ChecksumInterval = 5;

        var network = new LockstepLoopbackNetwork(randomSeed: 7) { Latency = 0.04, Jitter = 0.03 };
        var server = new LockstepServer(settings, network.ServerTransport);
        network.AttachServer(server);
        var reports = new List<LockstepDesyncReport>();
        server.DesyncDetectedEvent += reports.Add;

        var clients = new List<LockstepClient>();
        for (var id = 1; id <= 3; id++)
        {
            var client = new LockstepClient(LockstepClientSettings.Default, network.CreateClientTransport(id));
            network.AttachClient(id, client);
            client.Join();
            clients.Add(client);
        }

        try
        {
            var random = new System.Random(1);
            var time = 0.0;
            for (var frame = 0; frame < 1500; frame++)
            {
                time += 0.008 + random.NextDouble() * 0.017;
                network.Deliver(time);
                foreach (var client in clients)
                {
                    client.SetInput(ScriptedInput(client.LocalSlot, frame));
                    client.Update(time);
                }
                network.Deliver(time);
                server.Update(time);
                network.Deliver(time);
            }

            Assert.IsEmpty(reports, "desync reported");
            Assert.Greater(server.VerifiedChecksumCount, 10, "too few checksum rounds compared");
            AssertSameChecksums(clients[0], clients[1]);
            AssertSameChecksums(clients[0], clients[2]);
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
            server.Dispose();
        }
    }

    // Different players press different things over time, so their states really diverge if anything is wrong.
    // The slot is -1 until the server accepted the client.
    private static GameInput ScriptedInput(int slot, int frame)
    {
        var phase = frame / 15 + (slot + 1) * 7;
        return new GameInput
        {
            moveX = (sbyte)(phase % 3 * 100 - 100),
            moveY = (sbyte)((phase / 3) % 3 * 100 - 100),
            buttons = (byte)((phase % 4 == 0) ? GameInput.FIRE : 0),
        };
    }

    private static void AssertSameChecksums(LockstepClient a, LockstepClient b)
    {
        var byTick = new Dictionary<int, ulong>();
        foreach (var pair in a.LocalChecksums)
        {
            byTick[pair.Key] = pair.Value;
        }
        var compared = 0;
        foreach (var pair in b.LocalChecksums)
        {
            if (byTick.TryGetValue(pair.Key, out var hash))
            {
                Assert.AreEqual(hash, pair.Value, $"state differs at tick {pair.Key}");
                compared++;
            }
        }
        Assert.Greater(compared, 0);
    }
}
```

Add `client.AddCommand(...)` calls in the loop to cover commands, and add a fourth client later in the run to cover
late join.

## Two simulations from the same frames

Steps two simulation worlds in one process with the frames of a recorded match and compares the whole state after
every tick. Fast, precise (the first bad tick), and independent of the network code.

```csharp
[Test]
public unsafe void SameFrames_GiveTheSameState()
{
    var bytes = System.IO.File.ReadAllBytes("Assets/Tests/Replays/match-01.lockstep");
    using (var replay = LockstepReplay.Read(bytes))
    using (var a = new LockstepSimulation(replay.Config))
    using (var b = new LockstepSimulation(replay.Config))
    {
        for (var tick = replay.Frames.FirstTick; tick < replay.Frames.EndTick; tick++)
        {
            replay.Frames.TryGet(tick, out var frame, out var length);
            a.Step(frame, length);
            b.Step(frame, length);
            Assert.AreEqual(a.ComputeChecksum(), b.ComputeChecksum(), $"state differs at tick {tick}");
        }
    }
}
```

Record the replay from a real match (`client.ExportReplay()` or the debug window). Pass the game's
`LockstepSimulationOptions` to both simulations when the simulation needs the prefab registry.

## Golden replay

Fails as soon as a change alters the simulation, which also means old replays and mixed builds stop working. When
the change is intended, re-record the replay.

```csharp
[Test]
public void GoldenReplay_StillMatches()
{
    var bytes = System.IO.File.ReadAllBytes("Assets/Tests/Replays/match-01.lockstep");
    using (var replay = LockstepReplay.Read(bytes))
    using (var player = new LockstepReplayPlayer(replay))
    {
        Assert.AreEqual(-1, player.SimulateToEnd(), "the simulation diverges from the recording at this tick");
    }
}
```

## Isolating a few systems

Create the clients or simulations with explicit systems to test one mechanic without the rest of the game:

```csharp
var options = new LockstepSimulationOptions
{
    AutoDiscoverSystems = false,
    AdditionalSystems = new[] { typeof(AvatarSpawnSystem), typeof(AvatarMoveSystem) },
};
var client = new LockstepClient(LockstepClientSettings.Default, network.CreateClientTransport(id), options);
```

Systems used only by tests should carry `[DisableAutoCreation]` so they never join real simulations; add them through
`AdditionalSystems`.
