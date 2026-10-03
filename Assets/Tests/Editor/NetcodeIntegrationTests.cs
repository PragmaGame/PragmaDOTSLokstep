using System.Collections.Generic;
using NUnit.Framework;
using Pragma.Lockstep.Netcode;
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

        [Test]
        public void ServerAndClients_RunASessionOverRpcs()
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

            var endpoint = NetworkEndpoint.LoopbackIpv4.WithPort(7971);
            Assert.IsTrue(LockstepNetcode.Listen(server, endpoint));
            LockstepNetcode.Connect(clientA, endpoint);
            LockstepNetcode.Connect(clientB, endpoint);

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
    }
}
