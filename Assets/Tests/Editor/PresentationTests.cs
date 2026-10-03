using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Core;
using Unity.Entities;
using Unity.Transforms;

namespace Pragma.Lockstep.Tests
{
    public class PresentationTests
    {
        [Test]
        public void ViewSystem_SpawnsMovesAndRemovesViews()
        {
            using (var world = new World("Presentation"))
            {
                var entityManager = world.EntityManager;
                var prefab = entityManager.CreateEntity(typeof(Prefab), typeof(LocalTransform), typeof(LockstepTransform), typeof(LockstepTransformPrevious));
                entityManager.SetComponentData(prefab, LocalTransform.Identity);
                entityManager.SetComponentData(prefab, LockstepTransform.Identity);
                var registry = entityManager.CreateEntity(typeof(LockstepPrefabRegistry), typeof(LockstepPrefabElement));
                entityManager.GetBuffer<LockstepPrefabElement>(registry).Add(new LockstepPrefabElement { prefab = prefab });
                var viewSystem = world.GetOrCreateSystemManaged<LockstepViewSystem>();

                var settings = LockstepServerSettings.Default;
                settings.InputSize = UnsafeUtility.SizeOf<TestInput>();
                settings.StartDelaySeconds = 0;
                var network = new LockstepLoopbackNetwork();
                var server = new LockstepServer(settings, network.ServerTransport);
                network.AttachServer(server);
                var options = LockstepClientWorldUtility.CreateSimulationOptions(world, false);
                options.AutoDiscoverSystems = false;
                options.AdditionalSystems = new[] { typeof(TestSpawnSystem) };
                var client = new LockstepClient(LockstepClientSettings.Default, network.CreateClientTransport(1), options);
                network.AttachClient(1, client);
                LockstepWorlds.RegisterClient(world, client);
                client.Join();

                try
                {
                    var time = 0.0;
                    var sawView = false;
                    var lastX = float.MinValue;
                    while (client.SimulatedTicks < 40)
                    {
                        time += 1 / 60.0;
                        network.Deliver(time);
                        client.Update(time);
                        network.Deliver(time);
                        server.Update(time);
                        network.Deliver(time);
                        viewSystem.Update();

                        var tick = client.SimulatedTicks - 1;
                        if (tick >= 5 && tick < 29)
                        {
                            Assert.AreEqual(1, viewSystem.ViewCount, $"tick {tick}");
                            using (var query = entityManager.CreateEntityQuery(typeof(LockstepView), typeof(LocalTransform)))
                            {
                                var view = query.GetSingletonEntity();
                                var x = entityManager.GetComponentData<LocalTransform>(view).Position.x;
                                Assert.GreaterOrEqual(x, lastX - 1e-4f, "the view moves forward smoothly");
                                Assert.That(x, Is.InRange(10f, 11f + tick - 3 + 1e-3f));
                                lastX = x;
                                sawView = true;
                            }
                        }
                        if (tick >= 31)
                        {
                            Assert.AreEqual(0, viewSystem.ViewCount, "the view goes away with the simulated entity");
                        }
                    }
                    Assert.IsTrue(sawView);
                    Assert.AreEqual(1, TestUtility.Count<LockstepPrefabRegistry>(client.Simulation.World.EntityManager), "the registry was copied into the simulation");
                }
                finally
                {
                    LockstepWorlds.UnregisterClient(world);
                    client.Dispose();
                    server.Dispose();
                }
            }
        }

        [Test]
        public void OfflineSystem_RunsASessionInsideAWorld()
        {
            using (var world = new World("Offline", WorldFlags.Game))
            {
                DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, typeof(LockstepInputSystemGroup), typeof(LockstepOfflineSystem));
                var entityManager = world.EntityManager;
                entityManager.CreateSingleton(LockstepOfflineConfig.Create<TestInput>(30));

                var time = 0.0;
                for (var frame = 0; frame < 120; frame++)
                {
                    using (var query = entityManager.CreateEntityQuery(typeof(LockstepLocalInput)))
                    {
                        var localInput = query.GetSingleton<LockstepLocalInput>();
                        localInput.Set(new TestInput { moveX = 1 });
                        query.SetSingleton(localInput);
                    }
                    time += 1 / 60.0;
                    world.SetTime(new TimeData(time, 1 / 60f));
                    world.Update();
                }

                Assert.IsTrue(LockstepWorlds.TryGetClient(world, out var client));
                Assert.AreEqual(LockstepClientState.Running, client.State);
                Assert.Greater(client.SimulatedTicks, 50);
                Assert.AreEqual(1, TestUtility.Count<LockstepPlayer>(client.Simulation.World.EntityManager));
                var input = client.Simulation.World.EntityManager.GetComponentData<LockstepPlayerInput>(TestUtility.PlayerEntity(client.Simulation, 0));
                Assert.AreEqual(1, input.Get<TestInput>().moveX, "local input reached the simulation");

                using (var query = entityManager.CreateEntityQuery(typeof(LockstepOfflineConfig)))
                {
                    entityManager.DestroyEntity(query);
                }
                world.Update();
                Assert.IsFalse(LockstepWorlds.TryGetClient(world, out _), "removing the config ends the session");
            }
        }

        [Test]
        public void LocalInputEntity_SendsCommandsWithTheirData()
        {
            using (var world = new World("Offline", WorldFlags.Game))
            {
                DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, typeof(LockstepInputSystemGroup), typeof(LockstepOfflineSystem));
                var entityManager = world.EntityManager;
                entityManager.CreateSingleton(LockstepOfflineConfig.Create<TestInput>(30));

                using (var query = entityManager.CreateEntityQuery(typeof(LockstepLocalInput), typeof(LockstepCommand), typeof(LockstepCommandData)))
                {
                    var time = 0.0;
                    for (var frame = 0; frame < 120; frame++)
                    {
                        if (frame == 30)
                        {
                            var entity = query.GetSingletonEntity();
                            var data = entityManager.GetBuffer<LockstepCommandData>(entity);
                            var commands = entityManager.GetBuffer<LockstepCommand>(entity);
                            using (var first = new NativeArray<int>(new[] { 4, 5, 6 }, Allocator.Temp))
                            using (var second = new NativeArray<int>(new[] { 7 }, Allocator.Temp))
                            {
                                commands.Add(LockstepCommand.Create(new TestCommand { value = 2 }, first, data));
                                commands.Add(LockstepCommand.Create(new TestCommand { value = 3 }, second, data));
                            }
                        }
                        time += 1 / 60.0;
                        world.SetTime(new TimeData(time, 1 / 60f));
                        world.Update();
                    }
                    Assert.AreEqual(0, entityManager.GetBuffer<LockstepCommandData>(query.GetSingletonEntity()).Length, "sent data is cleared");
                }

                // The session runs without test systems: replay what it recorded with them.
                Assert.IsTrue(LockstepWorlds.TryGetClient(world, out var client));
                using (var replay = LockstepReplay.Read(client.ExportReplay()))
                using (var player = new LockstepReplayPlayer(replay, TestUtility.Options(typeof(TestGameplaySystem))) { VerifyChecksums = false })
                {
                    player.SimulateToEnd();
                    var state = TestUtility.PlayerState(player.Simulation, 0);
                    Assert.AreEqual(5, state.commandSum);
                    Assert.AreEqual(4, state.commandDataCount);
                    Assert.AreEqual(22, state.commandDataSum);
                    Assert.AreEqual(((4u * 31 + 5) * 31 + 6) * 31 + 7, state.commandDataHash, "each command kept its own data");
                }
            }
        }
    }
}
