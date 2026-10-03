using System.Collections.Generic;
using NUnit.Framework;
using Pragma.Lockstep.Netcode;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Core;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;


namespace Pragma.Lockstep.Tests
{
    /// <summary>A real Netcode for Entities server and clients in one process, connected through the IPC driver.</summary>
    public class NetcodeIntegrationTests
    {
        private readonly List<World> _worlds = new List<World>();
        private double _time;
        private bool _runInBackground;

        [SetUp]
        public void SetUp()
        {
            // Netcode logs an error when the application would stall without focus.
            _runInBackground = UnityEngine.Application.runInBackground;
            UnityEngine.Application.runInBackground = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var world in _worlds)
            {
                if (world.IsCreated)
                {
                    world.Dispose();
                }
            }
            _worlds.Clear();
            UnityEngine.Application.runInBackground = _runInBackground;
        }

        private World Track(World world)
        {
            // Time is driven by the test, not by the editor frame.
            var timeSystem = world.GetExistingSystemManaged<UpdateWorldTimeSystem>();
            if (timeSystem != null)
            {
                timeSystem.Enabled = false;
            }
            // Game input systems of the project would write their own input structs into this test session.
            var inputGroup = world.GetExistingSystemManaged<LockstepInputSystemGroup>();
            if (inputGroup != null)
            {
                inputGroup.Enabled = false;
            }
            _worlds.Add(world);
            return world;
        }

        private void Tick(int frames, double deltaTime = 1 / 60.0)
        {
            for (var i = 0; i < frames; i++)
            {
                _time += deltaTime;
                foreach (var world in _worlds)
                {
                    world.SetTime(new TimeData(_time, (float)deltaTime));
                    world.Update();
                }
            }
        }

        /// <summary>A server world hosting a session for two players and two client worlds joining it.</summary>
        private (World Server, World ClientA, World ClientB) StartSession(ushort port)
        {
            var server = Track(ClientServerBootstrap.CreateServerWorld("Lockstep Test Server"));
            var clientA = Track(ClientServerBootstrap.CreateClientWorld("Lockstep Test Client A"));
            var clientB = Track(ClientServerBootstrap.CreateClientWorld("Lockstep Test Client B"));

            var settings = LockstepServerSettings.Default;
            settings.TickRate = 30;
            settings.InputSize = UnsafeUtility.SizeOf<TestInput>();
            settings.MinPlayersToStart = 2;
            settings.ChecksumInterval = 10;
            settings.StartDelaySeconds = 0.2f;
            LockstepNetcode.HostSession(server, settings);
            LockstepNetcode.JoinSession(clientA, LockstepClientSettings.Default);
            LockstepNetcode.JoinSession(clientB, LockstepClientSettings.Default);

            var endpoint = NetworkEndpoint.LoopbackIpv4.WithPort(port);
            Assert.IsTrue(LockstepNetcode.Listen(server, endpoint));
            LockstepNetcode.Connect(clientA, endpoint);
            LockstepNetcode.Connect(clientB, endpoint);
            return (server, clientA, clientB);
        }

        [Test]
        public void ServerAndClients_RunASessionOverRpcs()
        {
            var (server, clientA, clientB) = StartSession(7971);

            var desyncs = 0;
            Tick(30);
            Assert.IsTrue(LockstepNetcode.TryGetServer(server, out var lockstepServer), "the server session is up");
            lockstepServer.DesyncDetectedEvent += _ => desyncs++;

            Tick(60 * 6);

            Assert.IsTrue(LockstepNetcode.TryGetClient(clientA, out var a), "client A joined");
            Assert.IsTrue(LockstepNetcode.TryGetClient(clientB, out var b), "client B joined");
            Assert.AreEqual(LockstepClientState.Running, a.State);
            Assert.AreEqual(LockstepClientState.Running, b.State);
            Assert.AreNotEqual(a.LocalSlot, b.LocalSlot);
            Assert.Greater(a.SimulatedTicks, 120);
            Assert.Greater(b.SimulatedTicks, 120);
            Assert.AreEqual(2, TestUtility.Count<LockstepPlayer>(a.Simulation.World.EntityManager));
            Assert.Greater(lockstepServer.VerifiedChecksumCount, 5);
            Assert.AreEqual(0, desyncs);

            // Removing the config leaves the session; a new config joins again over the same connection.
            using (var query = clientB.EntityManager.CreateEntityQuery(typeof(LockstepClientConfig)))
            {
                clientB.EntityManager.DestroyEntity(query);
            }
            Tick(60);
            Assert.AreEqual(1, lockstepServer.PlayerCount, "the leave freed the slot");
            Assert.AreEqual(1, TestUtility.Count<LockstepPlayer>(a.Simulation.World.EntityManager));

            LockstepNetcode.JoinSession(clientB, LockstepClientSettings.Default);
            Tick(60 * 3);
            Assert.IsTrue(LockstepNetcode.TryGetClient(clientB, out var rejoined), "client B joined again");
            Assert.AreEqual(LockstepClientState.Running, rejoined.State);
            Assert.AreEqual(2, TestUtility.Count<LockstepPlayer>(a.Simulation.World.EntityManager));

            // Disconnecting a client is announced to the other one.
            using (var query = clientB.EntityManager.CreateEntityQuery(typeof(NetworkStreamConnection)))
            {
                clientB.EntityManager.AddComponent<NetworkStreamRequestDisconnect>(query);
            }
            Tick(60);
            Assert.AreEqual(1, TestUtility.Count<LockstepPlayer>(a.Simulation.World.EntityManager));
            Assert.IsFalse(LockstepNetcode.TryGetClient(clientB, out _), "the disconnected client disposed its session");
        }

        [Test]
        public void ACommandLargerThanTheReliableWindow_ReachesEveryClient()
        {
            var (_, clientA, clientB) = StartSession(7972);
            Tick(60 * 3);
            Assert.IsTrue(LockstepNetcode.TryGetClient(clientA, out var a), "client A joined");
            Assert.IsTrue(LockstepNetcode.TryGetClient(clientB, out var b), "client B joined");
            Assert.AreEqual(LockstepClientState.Running, a.State);
            Assert.AreEqual(LockstepClientState.Running, b.State);

            // 128 KB: 128 RPCs of 1 KB each way, several times what the reliable pipeline sends without acknowledgement.
            const int count = 32 * 1024;
            var values = new int[count];
            var sum = 0L;
            for (var i = 0; i < count; i++)
            {
                values[i] = i * 7;
                sum += values[i];
            }
            using (var data = new NativeArray<int>(values, Allocator.Temp))
            {
                a.AddCommand(new TestCommand { value = 1 }, data);
            }
            Tick(60 * 3);

            Assert.AreEqual(LockstepClientState.Running, a.State);
            Assert.AreEqual(LockstepClientState.Running, b.State);
            Assert.IsFalse(a.IsDesynced || b.IsDesynced);
            foreach (var client in new[] { a, b })
            {
                // The sessions run without test systems: replay what each client received with them.
                using (var replay = LockstepReplay.Read(client.ExportReplay()))
                using (var player = new LockstepReplayPlayer(replay, TestUtility.Options(typeof(TestGameplaySystem))) { VerifyChecksums = false })
                {
                    player.SimulateToEnd();
                    var state = TestUtility.PlayerState(player.Simulation, a.LocalSlot);
                    Assert.AreEqual(count, state.commandDataCount);
                    Assert.AreEqual(sum, state.commandDataSum);
                }
            }
        }
    }
}
