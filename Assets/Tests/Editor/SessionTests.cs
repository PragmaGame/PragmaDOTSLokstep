using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TestTools;

namespace Pragma.Lockstep.Tests
{
    public class SessionTests
    {
        private static LockstepServerSettings Settings(int minPlayers = 2, int tickRate = 30)
        {
            var settings = LockstepServerSettings.Default;
            settings.TickRate = tickRate;
            settings.MaxPlayers = 4;
            settings.InputSize = UnsafeUtility.SizeOf<TestInput>();
            settings.Seed = 1234;
            settings.MinPlayersToStart = minPlayers;
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

        private static void AssertSameChecksums(LockstepClient a, LockstepClient b, int minimumCommonTicks)
        {
            var byTick = new Dictionary<int, ulong>();
            foreach (var pair in a.LocalChecksums)
            {
                byTick[pair.Key] = pair.Value;
            }
            var common = 0;
            foreach (var pair in b.LocalChecksums)
            {
                if (!byTick.TryGetValue(pair.Key, out var hash))
                {
                    continue;
                }
                Assert.AreEqual(hash, pair.Value, $"state differs at tick {pair.Key}");
                common++;
            }
            Assert.GreaterOrEqual(common, minimumCommonTicks);
        }

        [Test]
        public void TwoClients_StayInSyncThroughLatencyAndJitter()
        {
            using (var session = new SessionHarness(Settings(), latency: 0.05, jitter: 0.04))
            {
                var a = session.AddClient();
                var b = session.AddClient();
                session.Run(10, VaryingInput);

                Assert.AreEqual(LockstepClientState.Running, a.State);
                Assert.AreEqual(LockstepClientState.Running, b.State);
                Assert.Greater(a.SimulatedTicks, 250);
                Assert.Greater(b.SimulatedTicks, 250);
                Assert.IsEmpty(session.Desyncs);
                Assert.IsFalse(a.IsDesynced || b.IsDesynced);
                Assert.Greater(session.Server.VerifiedChecksumCount, 20);
                AssertSameChecksums(a, b, 20);

                var stateA = TestUtility.PlayerState(a.Simulation, b.LocalSlot);
                Assert.Greater(stateA.movingTicks, 0, "inputs of the other player reached this client");
                Assert.Greater(stateA.jumps, 0);
            }
        }

        [Test]
        public void InputClock_ConvergesSoInputsArriveInTime()
        {
            using (var session = new SessionHarness(Settings(minPlayers: 1), latency: 0.15, jitter: 0.02))
            {
                var client = session.AddClient();
                session.Run(6, VaryingInput);
                var lateAfterWarmup = session.Server.LateInputCount;
                session.Run(6, VaryingInput);

                Assert.AreEqual(lateAfterWarmup, session.Server.LateInputCount, "no input should be late once the clock has converged");
                var lead = client.LastSentInputTick - session.Server.ClosedTicks;
                Assert.That(lead, Is.InRange(4, 12), "inputs are sent about one-way latency plus margin ahead");
                Assert.That(client.BufferedTicks, Is.InRange(0, 6));
            }
        }

        [Test]
        public void LateJoiner_ReplaysTheMatchAndMatchesTheOthers()
        {
            using (var session = new SessionHarness(Settings(minPlayers: 1), latency: 0.03, jitter: 0.02))
            {
                var first = session.AddClient();
                session.Run(4, VaryingInput);
                var late = session.AddClient();
                session.Run(4, VaryingInput);

                Assert.AreEqual(LockstepClientState.Running, late.State);
                Assert.Greater(late.JoinTick, 100);
                Assert.That(first.SimulatedTicks - late.SimulatedTicks, Is.InRange(-3, 3), "the late joiner caught up");
                Assert.IsEmpty(session.Desyncs);
                AssertSameChecksums(first, late, 20);
            }
        }

        [Test]
        public void StartData_ReachesTheSimulation()
        {
            var settings = Settings(minPlayers: 1);
            settings.StartData = LockstepBytes.ToFixedList128(new TestCommand { value = 42 });
            using (var session = new SessionHarness(settings, latency: 0.02))
            {
                var client = session.AddClient();
                session.Run(1, VaryingInput);

                Assert.AreEqual(LockstepClientState.Running, client.State);
                Assert.AreEqual(settings.StartData.Length, client.Config.StartData.Length);
                using (var query = client.Simulation.World.EntityManager.CreateEntityQuery(typeof(LockstepSessionInfo)))
                {
                    Assert.AreEqual(42, query.GetSingleton<LockstepSessionInfo>().GetStartData<TestCommand>().value);
                }
            }
        }

        [Test]
        public void Disconnect_IsAnnouncedAsLeave()
        {
            using (var session = new SessionHarness(Settings(), latency: 0.02))
            {
                var stays = session.AddClient();
                var leaves = session.AddClient();
                session.Run(2, VaryingInput);
                var leftSlot = leaves.LocalSlot;
                Assert.AreEqual(2, TestUtility.Count<LockstepPlayer>(stays.Simulation.World.EntityManager));

                session.Disconnect(leaves);
                session.Run(1, VaryingInput);

                Assert.AreEqual(1, TestUtility.Count<LockstepPlayer>(stays.Simulation.World.EntityManager));
                Assert.AreEqual(Unity.Entities.Entity.Null, TestUtility.PlayerEntity(stays.Simulation, leftSlot));
                Assert.AreEqual(1, session.Server.PlayerCount);
            }
        }

        [Test]
        public void Commands_AreDeliveredExactlyOnce()
        {
            using (var session = new SessionHarness(Settings(), latency: 0.06, jitter: 0.05))
            {
                var a = session.AddClient();
                var b = session.AddClient();
                var sent = 0;
                session.Run(6, (client, frame) =>
                {
                    VaryingInput(client, frame);
                    if (client == a && frame % 37 == 0 && client.State == LockstepClientState.Running)
                    {
                        client.AddCommand(new TestCommand { value = frame });
                        sent += frame;
                    }
                });
                session.Run(2, VaryingInput);

                Assert.Greater(sent, 0);
                Assert.AreEqual(sent, TestUtility.PlayerState(a.Simulation, a.LocalSlot).commandSum);
                Assert.AreEqual(sent, TestUtility.PlayerState(b.Simulation, a.LocalSlot).commandSum);
            }
        }

        [Test]
        public void Desync_IsDetectedAndAttributedToTheOddClient()
        {
            using (var session = new SessionHarness(Settings(minPlayers: 3), latency: 0.02))
            {
                var a = session.AddClient();
                session.AddClient();
                var odd = session.AddClient(TestUtility.Options(typeof(TestGameplaySystem), typeof(TestDesyncSystem)));
                var events = new List<(int Tick, bool Local)>();
                odd.DesyncedEvent += (tick, local) => events.Add((tick, local));
                a.DesyncedEvent += (tick, local) => events.Add((tick, local));

                // Every client logs an error per desync report; that is the expected outcome here.
                LogAssert.ignoreFailingMessages = true;
                try
                {
                    session.Run(2.5, VaryingInput);
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = false;
                }

                Assert.IsNotEmpty(session.Desyncs);
                Assert.AreEqual(1UL << odd.LocalSlot, session.Desyncs[0].slotMask);
                Assert.IsTrue(odd.IsDesynced);
                Assert.IsTrue(a.IsDesynced, "every client is told");
                Assert.Contains((session.Desyncs[0].tick, true), events);
                Assert.Contains((session.Desyncs[0].tick, false), events);
            }
        }

        [Test]
        public void FullSession_RejectsExtraPlayers()
        {
            var settings = Settings(minPlayers: 1);
            settings.MaxPlayers = 1;
            using (var session = new SessionHarness(settings))
            {
                session.AddClient();
                LogAssert.Expect(LogType.Warning, new Regex("SessionFull"));
                var extra = session.AddClient();
                session.Run(0.5);
                Assert.AreEqual(LockstepClientState.Rejected, extra.State);
                Assert.AreEqual(LockstepJoinRejectReason.SessionFull, extra.RejectReason);
            }
        }

        [Test]
        public void Replay_ReproducesTheMatch()
        {
            using (var session = new SessionHarness(Settings(), latency: 0.04, jitter: 0.03))
            {
                var a = session.AddClient();
                session.AddClient();
                session.Run(5, VaryingInput);

                var bytes = a.ExportReplay();
                using (var replay = LockstepReplay.Read(bytes))
                using (var player = new LockstepReplayPlayer(replay, TestUtility.Options(typeof(TestGameplaySystem))))
                {
                    Assert.Greater(replay.Checksums.Count, 10);
                    Assert.AreEqual(-1, player.SimulateToEnd());
                    Assert.AreEqual(a.ConfirmedTicks, player.Simulation.Tick);
                }

                var serverReplay = session.Server.ExportReplay();
                using (var replay = LockstepReplay.Read(serverReplay))
                using (var player = new LockstepReplayPlayer(replay, TestUtility.Options(typeof(TestGameplaySystem))))
                {
                    Assert.AreEqual(-1, player.SimulateToEnd(), "server-side recordings verify against the agreed checksums");
                }
            }
        }

        [Test]
        public void MaxInputWait_HoldsFramesForSlowInputs()
        {
            var settings = Settings(minPlayers: 1);
            settings.MaxInputWaitTicks = 30;
            using (var session = new SessionHarness(settings, latency: 0.05))
            {
                session.AddClient();
                session.Run(3, VaryingInput);
                var closedBefore = session.Server.ClosedTicks;

                // The client freezes; without waiting the server would close 15 ticks in half a second.
                session.StepServerOnly(0.5);
                Assert.LessOrEqual(session.Server.ClosedTicks, closedBefore + 8, "frames are held while the input is missing");

                // After MaxInputWaitTicks the missing input is repeated and ticks close again, 30 ticks behind.
                session.StepServerOnly(1.5);
                Assert.GreaterOrEqual(session.Server.ClosedTicks, closedBefore + 25);
            }
        }
    }
}
