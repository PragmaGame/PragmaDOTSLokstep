using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace Pragma.Lockstep.Tests
{
    /// <summary>
    /// <see cref="LockstepReplayHost"/> plays a recorded match to ordinary clients: the same states as the recording, the
    /// slot each connection watches, speed and pause, the end of the match, checks against the recorded checksums, and
    /// <see cref="LockstepReplaySession"/> showing the replay in a world.
    /// </summary>
    public class ReplayHostTests
    {
        private const double FRAME = 1.0 / 60;

        private static LockstepServerSettings Settings()
        {
            var settings = LockstepServerSettings.Default;
            settings.TickRate = 30;
            settings.MaxPlayers = 4;
            settings.InputSize = UnsafeUtility.SizeOf<TestInput>();
            settings.Seed = 1234;
            settings.MinPlayersToStart = 2;
            settings.StartDelaySeconds = 0.3f;
            settings.ChecksumInterval = 10;
            return settings;
        }

        // Every client presses different keys over time, so the players really diverge.
        private static void VaryingInput(LockstepClient client, int frame)
        {
            if (client.LocalSlot < 0)
            {
                return;
            }
            var slot = client.LocalSlot + 1;
            client.SetInput(new TestInput
            {
                moveX = (frame / (7 * slot)) % 3 - 1,
                moveY = slot,
                buttons = (frame / (5 + slot)) % 2,
            });
        }

        /// <summary>A two-player match of a few seconds; with a late joiner the third player enters a second later.</summary>
        private static byte[] RecordMatch(bool hasLateJoiner = false)
        {
            using (var session = new SessionHarness(Settings(), latency: 0.03, jitter: 0.02))
            {
                var recorder = session.AddClient();
                session.AddClient();
                session.Run(2, VaryingInput);
                if (hasLateJoiner)
                {
                    session.AddClient();
                }
                session.Run(2, VaryingInput);
                return recorder.ExportReplay();
            }
        }

        private static ulong FinalChecksum(byte[] bytes)
        {
            using (var replay = LockstepReplay.Read(bytes))
            using (var player = new LockstepReplayPlayer(replay, TestUtility.Options(typeof(TestGameplaySystem))))
            {
                player.SimulateToEnd();
                return player.Simulation.ComputeChecksum();
            }
        }

        private sealed class Viewing : System.IDisposable
        {
            private readonly LockstepLoopbackNetwork _network = new LockstepLoopbackNetwork();

            public Viewing(byte[] bytes)
            {
                Replay = LockstepReplay.Read(bytes);
                Host = new LockstepReplayHost(Replay, _network.ServerTransport);
                _network.AttachServer(Host);
            }

            public LockstepReplay Replay { get; }
            public LockstepReplayHost Host { get; }
            public System.Collections.Generic.List<LockstepClient> Clients { get; } = new System.Collections.Generic.List<LockstepClient>();
            public double Time { get; private set; } = 10;

            public LockstepClient AddClient(int? slot, params System.Type[] systems)
            {
                var connectionId = Clients.Count + 1;
                var settings = LockstepClientSettings.Default;
                settings.PlayoutDelayTicks = 0.5f;
                var client = new LockstepClient(settings, _network.CreateClientTransport(connectionId),
                    TestUtility.Options(systems.Length > 0 ? systems : new[] { typeof(TestGameplaySystem) }));
                if (slot.HasValue)
                {
                    Host.Watch(connectionId, slot.Value);
                }
                _network.SetTime(Time);
                _network.AttachClient(connectionId, client);
                client.Join();
                Clients.Add(client);
                return client;
            }

            public void SetSpeed(float speed, bool isPaused = false)
            {
                Host.Speed = speed;
                Host.IsPaused = isPaused;
                foreach (var client in Clients)
                {
                    client.PlaybackSpeed = isPaused ? 0f : speed;
                }
            }

            public void Run(double seconds)
            {
                var end = Time + seconds;
                while (Time < end)
                {
                    Time += FRAME;
                    _network.Deliver(Time);
                    foreach (var client in Clients)
                    {
                        client.Update(Time);
                    }
                    _network.Deliver(Time);
                    Host.Update(Time);
                    _network.Deliver(Time);
                }
            }

            /// <summary>Until the host released everything and the clients simulated it, or the time is up.</summary>
            public void RunToEnd(double maxSeconds = 30)
            {
                var end = Time + maxSeconds;
                while (Time < end && (!Host.IsFinished || Clients.Exists(client => client.SimulatedTicks < Replay.FrameCount)))
                {
                    Run(FRAME);
                }
            }

            public void Dispose()
            {
                foreach (var client in Clients)
                {
                    client.Dispose();
                }
                Host.Dispose();
                Replay.Dispose();
            }
        }

        [Test]
        public void ReplayHost_PlaysTheMatchToAClient()
        {
            var bytes = RecordMatch();

            using (var viewing = new Viewing(bytes))
            {
                var client = viewing.AddClient(0);
                viewing.RunToEnd();

                Assert.AreEqual(LockstepClientState.Ended, client.State, "the host ends the session after the last frame");
                Assert.AreEqual(viewing.Replay.FrameCount, client.SimulatedTicks);
                Assert.AreEqual(FinalChecksum(bytes), client.Simulation.ComputeChecksum(), "the client reaches the recorded state");
                Assert.AreEqual(-1, viewing.Host.FirstMismatchTick);
                Assert.Greater(client.LocalChecksums.Count, 10, "the client reported checksums the host compared");
                Assert.IsFalse(client.IsDesynced);
            }
        }

        [Test]
        public void ReplayHost_GivesEachConnectionTheSlotItWatches()
        {
            using (var viewing = new Viewing(RecordMatch()))
            {
                var second = viewing.AddClient(1);
                var first = viewing.AddClient(0);
                viewing.Run(0.5);

                Assert.AreEqual(1, second.LocalSlot);
                Assert.AreEqual(0, first.LocalSlot);
                Assert.AreEqual(LockstepClientState.Running, first.State);
            }
        }

        [Test]
        public void ReplayHost_RejectsAConnectionWithoutASlot()
        {
            using (var viewing = new Viewing(RecordMatch()))
            {
                LogAssert.Expect(LogType.Warning, new Regex("without a slot"));
                LogAssert.Expect(LogType.Warning, new Regex("InvalidRequest"));
                var client = viewing.AddClient(null);
                viewing.Run(0.2);

                Assert.AreEqual(LockstepClientState.Rejected, client.State);
                Assert.AreEqual(LockstepJoinRejectReason.InvalidRequest, client.RejectReason);
            }
        }

        [Test]
        public void ReplayHost_PlaysAtItsSpeedAndHoldsWhilePaused()
        {
            using (var viewing = new Viewing(RecordMatch()))
            {
                var client = viewing.AddClient(0);
                viewing.SetSpeed(2f);
                viewing.Run(1);

                var tickRate = viewing.Replay.Config.TickRate;
                Assert.AreEqual(2 * tickRate, viewing.Host.Position, 2, "twice as many ticks as seconds of real time");
                Assert.Greater(client.SimulatedTicks, 1.5 * tickRate, "the client follows the host at its speed");

                viewing.SetSpeed(2f, isPaused: true);
                viewing.Run(0.2);
                var position = viewing.Host.Position;
                var ticks = client.SimulatedTicks;
                var alpha = client.InterpolationAlpha;
                viewing.Run(1);

                Assert.AreEqual(position, viewing.Host.Position, "the clock stands while paused");
                Assert.AreEqual(ticks, client.SimulatedTicks, "and so does the client");
                Assert.AreEqual(alpha, client.InterpolationAlpha, "the picture holds still");

                viewing.SetSpeed(1f);
                viewing.RunToEnd();

                Assert.AreEqual(viewing.Replay.FrameCount, client.SimulatedTicks);
                Assert.AreEqual(-1, viewing.Host.FirstMismatchTick);
            }
        }

        [Test]
        public void ReplayHost_ReportsAStateThatDiffersFromTheRecording()
        {
            using (var viewing = new Viewing(RecordMatch()))
            {
                var mismatches = 0;
                viewing.Host.MismatchEvent += _ => mismatches++;
                LogAssert.Expect(LogType.Error, new Regex("Desync detected"));
                var client = viewing.AddClient(0, typeof(TestGameplaySystem), typeof(TestDesyncSystem));
                viewing.Run(2);

                Assert.GreaterOrEqual(viewing.Host.FirstMismatchTick, 0);
                Assert.AreEqual(1, mismatches, "reported once: every later state differs too");
                Assert.IsTrue(client.IsDesynced, "the client hears it as a desync of its own slot");
            }
        }

        [Test]
        public void Replay_ListsItsPlayers()
        {
            using (var replay = LockstepReplay.Read(RecordMatch(hasLateJoiner: true)))
            {
                Assert.AreEqual(3, replay.Players.Count);
                Assert.AreEqual(0, replay.Players[0].slot);
                Assert.AreEqual(1, replay.Players[1].slot);
                Assert.AreEqual(2, replay.Players[2].slot);
                Assert.AreEqual(replay.Players[0].joinTick, replay.Players[1].joinTick, "the first two entered together");
                Assert.Greater(replay.Players[2].joinTick, replay.Players[0].joinTick + 15, "the late joiner entered later");
                Assert.AreEqual(-1, replay.Players[2].leaveTick, "nobody left");
                Assert.AreEqual((double)replay.FrameCount / replay.Config.TickRate, replay.DurationSeconds, 1e-9);
            }
        }

        [Test]
        public void ReplaySession_ShowsTheReplayInAWorld()
        {
            var bytes = RecordMatch();
            var world = new World("Replay Viewer");

            try
            {
                using (var session = new LockstepReplaySession(LockstepReplay.Read(bytes)))
                {
                    var settings = LockstepClientSettings.Default;
                    settings.PlayoutDelayTicks = 0.5f;
                    var client = session.Watch(world, 1, settings, TestUtility.Options(typeof(TestGameplaySystem)));
                    session.Speed = 4f;

                    Assert.IsTrue(LockstepWorlds.TryGetClient(world, out var registered));
                    Assert.AreSame(client, registered);

                    var time = 10.0;
                    for (var i = 0; i < 600 && client.SimulatedTicks < session.Replay.FrameCount; i++)
                    {
                        time += FRAME;
                        session.Update(time);
                    }

                    Assert.AreEqual(1, client.LocalSlot);
                    Assert.AreEqual(session.Replay.FrameCount, client.SimulatedTicks, "four times faster: done well within ten seconds");
                    Assert.AreEqual(4f, client.PlaybackSpeed);
                }

                Assert.IsFalse(LockstepWorlds.TryGetClient(world, out _), "disposing the session unregisters its clients");
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}
