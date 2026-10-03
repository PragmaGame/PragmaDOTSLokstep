using System;
using NUnit.Framework;
using Pragma.Lockstep.Mathematics;
using Pragma.Lockstep.Navigation;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Pragma.Lockstep.Tests
{
    public class NavigationTests
    {
        private static readonly Type[] NavigationSystems =
        {
            typeof(LockstepNavSystemGroup),
            typeof(LockstepNavObstacleSystem),
            typeof(LockstepNavPathSystem),
            typeof(LockstepNavMoveSystem),
        };

        // 40 x 40 cells of half a unit: X and Z from -10 to 10.
        private static LockstepNavGrid CreateGrid(FixedPoint agentRadius = default)
        {
            return new LockstepNavGrid
            {
                origin = new FixedVector2(-10, -10),
                cellSize = FixedPoint.Half,
                width = 40,
                height = 40,
                agentRadius = agentRadius,
            };
        }

        private static FixedVector3 Point(FixedPoint x, FixedPoint z) => new FixedVector3(x, FixedPoint.Zero, z);

        private static FixedVector3 Point(FixedVector2 xz) => new FixedVector3(xz.x, FixedPoint.Zero, xz.y);

        private static void Block(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, FixedVector2 center, FixedVector2 size)
        {
            LockstepNavigation.Stamp(grid, cells, LockstepNavObstacleFootprint.Create(new LockstepNavObstacle { center = center, size = size }), 1);
        }

        // Every segment of the path, from the start on, crosses walkable cells only.
        private static void AssertWalkable(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, FixedVector3 start, NativeList<FixedVector3> waypoints)
        {
            var from = start;
            for (var i = 0; i < waypoints.Length; i++)
            {
                Assert.IsTrue(LockstepNavigation.HasLineOfSight(grid, cells, from, waypoints[i]), $"segment {i} to {waypoints[i]} crosses a blocked cell");
                from = waypoints[i];
            }
        }

        [Test]
        public void OpenGrid_PathGoesStraightToTheDestination()
        {
            var grid = CreateGrid();
            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
            using (var waypoints = new NativeList<FixedVector3>(Allocator.Temp))
            using (var pathfinder = new LockstepPathfinder(grid.CellCount, Allocator.Temp))
            {
                var destination = Point(FixedPoint.FromFraction(73, 10), 6);
                Assert.AreEqual(LockstepPathStatus.Complete, pathfinder.FindPath(grid, cells, Point(-8, -7), destination, waypoints));
                Assert.AreEqual(1, waypoints.Length);
                Assert.AreEqual(destination, waypoints[0]);
            }
        }

        [Test]
        public void Wall_IsWalkedAround()
        {
            var grid = CreateGrid();
            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
            using (var waypoints = new NativeList<FixedVector3>(Allocator.Temp))
            using (var pathfinder = new LockstepPathfinder(grid.CellCount, Allocator.Temp))
            {
                Block(grid, cells, FixedVector2.Zero, new FixedVector2(1, 12));
                var start = Point(-5, 0);
                var destination = Point(5, 0);

                Assert.AreEqual(LockstepPathStatus.Complete, pathfinder.FindPath(grid, cells, start, destination, waypoints));
                Assert.Greater(waypoints.Length, 1, "a straight line would cross the wall");
                Assert.AreEqual(destination, waypoints[waypoints.Length - 1]);
                AssertWalkable(grid, cells, start, waypoints);
                var isAround = false;
                for (var i = 0; i < waypoints.Length; i++)
                {
                    isAround |= FixedMath.Abs(waypoints[i].z) >= 6;
                }
                Assert.IsTrue(isAround, "the path turns around an end of the wall");
                Assert.LessOrEqual(waypoints.Length, 4, "string pulling leaves only a few corners");
            }
        }

        [Test]
        public void BlockedDestination_EndsAtTheNearestReachableCell()
        {
            var grid = CreateGrid();
            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
            using (var waypoints = new NativeList<FixedVector3>(Allocator.Temp))
            using (var pathfinder = new LockstepPathfinder(grid.CellCount, Allocator.Temp))
            {
                Block(grid, cells, FixedVector2.Zero, new FixedVector2(4, 4));
                var start = Point(-8, 0);
                var destination = Point(0, 0);

                Assert.AreEqual(LockstepPathStatus.Partial, pathfinder.FindPath(grid, cells, start, destination, waypoints));
                var end = waypoints[waypoints.Length - 1];
                Assert.IsTrue(LockstepNavigation.IsWalkable(grid, cells, end));
                Assert.LessOrEqual((double)FixedMath.Distance(end, destination), 2.5);
                AssertWalkable(grid, cells, start, waypoints);
            }
        }

        [Test]
        public void UnreachableDestination_EndsAsCloseAsPossible()
        {
            var grid = CreateGrid();
            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
            using (var waypoints = new NativeList<FixedVector3>(Allocator.Temp))
            using (var pathfinder = new LockstepPathfinder(grid.CellCount, Allocator.Temp))
            {
                // A closed box around (5, 5): the destination is walkable but out of reach.
                Block(grid, cells, new FixedVector2(3, 5), new FixedVector2(FixedPoint.Half, 5));
                Block(grid, cells, new FixedVector2(7, 5), new FixedVector2(FixedPoint.Half, 5));
                Block(grid, cells, new FixedVector2(5, 3), new FixedVector2(5, FixedPoint.Half));
                Block(grid, cells, new FixedVector2(5, 7), new FixedVector2(5, FixedPoint.Half));
                var start = Point(-8, -8);
                var destination = Point(5, 5);

                Assert.AreEqual(LockstepPathStatus.Partial, pathfinder.FindPath(grid, cells, start, destination, waypoints));
                var distance = (double)FixedMath.Distance(waypoints[waypoints.Length - 1], destination);
                Assert.That(distance, Is.InRange(2.0, 3.0), "the path ends just outside the box");
                AssertWalkable(grid, cells, start, waypoints);
            }
        }

        [Test]
        public void StartInsideObstacle_LeavesThroughTheNearestCell()
        {
            var grid = CreateGrid();
            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
            using (var waypoints = new NativeList<FixedVector3>(Allocator.Temp))
            using (var pathfinder = new LockstepPathfinder(grid.CellCount, Allocator.Temp))
            {
                Block(grid, cells, FixedVector2.Zero, new FixedVector2(4, 4));
                var destination = Point(8, 0);

                Assert.AreEqual(LockstepPathStatus.Complete, pathfinder.FindPath(grid, cells, Point(0, 0), destination, waypoints));
                Assert.AreEqual(Point(FixedPoint.FromFraction(9, 4), FixedPoint.FromFraction(1, 4)), waypoints[0], "first out of the box, to the nearest free cell");
                Assert.AreEqual(destination, waypoints[waypoints.Length - 1]);
                AssertWalkable(grid, cells, waypoints[0], waypoints);
            }
        }

        [Test]
        public void DiagonalWall_IsNotSqueezedThrough()
        {
            var grid = CreateGrid();
            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
            using (var waypoints = new NativeList<FixedVector3>(Allocator.Temp))
            using (var pathfinder = new LockstepPathfinder(grid.CellCount, Allocator.Temp))
            {
                // Cells touching only at their corners: a diagonal step between them would cut both.
                for (var i = 0; i < grid.width; i++)
                {
                    Block(grid, cells, grid.GetCellCenter(new int2(i, i)), new FixedVector2(FixedPoint.Half, FixedPoint.Half));
                }
                var start = Point(grid.GetCellCenter(new int2(5, 30)));

                Assert.AreEqual(LockstepPathStatus.Partial, pathfinder.FindPath(grid, cells, start, Point(grid.GetCellCenter(new int2(30, 5))), waypoints));
                for (var i = 0; i < waypoints.Length; i++)
                {
                    var cell = grid.WorldToCell(waypoints[i]);
                    Assert.Greater(cell.y, cell.x, $"waypoint {i} crossed the diagonal");
                }
            }
        }

        [Test]
        public void LineOfSight_ThroughACorner_NeedsBothSides()
        {
            var grid = CreateGrid();
            var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp);
            using (cells)
            {
                var from = grid.GetCellCenter(new int2(20, 20));
                var diagonal = grid.GetCellCenter(new int2(22, 22));
                Assert.IsTrue(LockstepNavigation.HasLineOfSight(grid, cells, from, diagonal));

                cells[grid.GetIndex(new int2(21, 20))] = new LockstepNavCell { blockers = 1 };
                Assert.IsFalse(LockstepNavigation.HasLineOfSight(grid, cells, from, diagonal), "the corner touches the blocked cell");
                Assert.IsFalse(LockstepNavigation.HasLineOfSight(grid, cells, diagonal, from), "both ways");
                Assert.IsFalse(LockstepNavigation.HasLineOfSight(grid, cells, from, grid.GetCellCenter(new int2(23, 21))), "a shallow line crosses it");
                Assert.IsTrue(LockstepNavigation.HasLineOfSight(grid, cells, from, grid.GetCellCenter(new int2(20, 25))), "a column beside it is free");
                Assert.IsTrue(LockstepNavigation.HasLineOfSight(grid, cells, from, grid.GetCellCenter(new int2(21, 25))), "a steep line passes it");
            }
        }

        [Test]
        public void Stamp_ThenRelease_LeavesTheCellsAsTheyWere()
        {
            var grid = CreateGrid(FixedPoint.FromFraction(1, 2));
            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
            {
                var obstacle = new LockstepNavObstacle { center = new FixedVector2(1, 0), size = new FixedVector2(3, 1) };
                var transform = LockstepTransform.FromPositionRotation(new FixedVector3(2, 0, -3), FixedQuaternion.RotateY(FixedMath.ToRadians(30)));
                var footprint = LockstepNavObstacleFootprint.Create(obstacle, transform);
                LockstepNavigation.Stamp(grid, cells, footprint, 1);
                LockstepNavigation.Stamp(grid, cells, footprint, 1);
                Assert.AreEqual(2, cells[grid.GetIndex(grid.WorldToCell(footprint.center))].blockers, "overlapping obstacles count");

                LockstepNavigation.Stamp(grid, cells, footprint, -2);
                for (var i = 0; i < cells.Length; i++)
                {
                    Assert.AreEqual(0, cells[i].blockers);
                }
            }
        }

        [Test]
        public void Burst_FindsTheSamePaths()
        {
            const int queries = 40;
            var grid = CreateGrid(FixedPoint.FromFraction(1, 4));
            var starts = new NativeArray<FixedVector3>(queries, Allocator.TempJob);
            var destinations = new NativeArray<FixedVector3>(queries, Allocator.TempJob);
            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.TempJob))
            using (starts)
            using (destinations)
            using (var managed = new NativeList<long>(Allocator.TempJob))
            using (var bursted = new NativeList<long>(Allocator.TempJob))
            {
                var random = new FixedRandom(99);
                for (var i = 0; i < 25; i++)
                {
                    var obstacle = new LockstepNavObstacle { size = new FixedVector2(random.NextFixedPoint(FixedPoint.Half, 4), random.NextFixedPoint(FixedPoint.Half, 4)) };
                    var position = Point(random.NextFixedPoint(-9, 9), random.NextFixedPoint(-9, 9));
                    var transform = LockstepTransform.FromPositionRotation(position, FixedQuaternion.RotateY(random.NextAngle()));
                    LockstepNavigation.Stamp(grid, cells, LockstepNavObstacleFootprint.Create(obstacle, transform), 1);
                }
                for (var i = 0; i < queries; i++)
                {
                    starts[i] = Point(random.NextFixedPoint(-10, 10), random.NextFixedPoint(-10, 10));
                    destinations[i] = Point(random.NextFixedPoint(-10, 10), random.NextFixedPoint(-10, 10));
                }

                new PathBatchJob { grid = grid, cells = cells, starts = starts, destinations = destinations, results = managed }.Execute();
                new PathBatchJob { grid = grid, cells = cells, starts = starts, destinations = destinations, results = bursted }.Run();

                Assert.AreEqual(managed.Length, bursted.Length);
                for (var i = 0; i < managed.Length; i++)
                {
                    Assert.AreEqual(managed[i], bursted[i], $"result value {i}");
                }
                Assert.Greater(managed.Length, queries * 5, "the paths have corners");
            }
        }

        [Test]
        public void Obstacles_BlockCellsWhileTheirEntitiesLive()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager);
                var obstacle = entityManager.CreateEntity(typeof(LockstepNavObstacle), typeof(LockstepTransform));
                entityManager.SetComponentData(obstacle, new LockstepNavObstacle { size = new FixedVector2(2, 2) });
                entityManager.SetComponentData(obstacle, LockstepTransform.FromPosition(new FixedVector3(-5, 0, 0)));

                TestUtility.Step(simulation);
                Assert.IsFalse(IsWalkable(entityManager, grid, Point(-5, 0)));
                Assert.IsTrue(IsWalkable(entityManager, grid, Point(5, 0)));
                var version = entityManager.GetComponentData<LockstepNavGrid>(grid).version;
                Assert.AreNotEqual(0u, version);

                TestUtility.Step(simulation);
                Assert.AreEqual(version, entityManager.GetComponentData<LockstepNavGrid>(grid).version, "nothing changed");

                entityManager.SetComponentData(obstacle, LockstepTransform.FromPosition(new FixedVector3(5, 0, 0)));
                TestUtility.Step(simulation);
                Assert.IsTrue(IsWalkable(entityManager, grid, Point(-5, 0)), "the obstacle released its old cells");
                Assert.IsFalse(IsWalkable(entityManager, grid, Point(5, 0)));
                Assert.AreNotEqual(version, entityManager.GetComponentData<LockstepNavGrid>(grid).version);

                entityManager.DestroyEntity(obstacle);
                TestUtility.Step(simulation);
                Assert.IsFalse(entityManager.Exists(obstacle), "the destroyed obstacle is gone once its cells are released");
                var cells = entityManager.GetBuffer<LockstepNavCell>(grid);
                for (var i = 0; i < cells.Length; i++)
                {
                    Assert.AreEqual(0, cells[i].blockers);
                }
            }
        }

        [Test]
        public void Agent_WalksAroundAWallAndArrives()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager, FixedPoint.Half);
                CreateWall(entityManager, FixedVector2.Zero, new FixedVector2(1, 12));
                var destination = Point(6, 0);
                var agent = CreateAgent(entityManager, Point(-6, 0), 4, destination);

                var ticks = WalkUntilArrived(simulation, grid, agent, 200);
                var state = entityManager.GetComponentData<LockstepNavAgent>(agent);
                Assert.AreEqual(LockstepNavStatus.Arrived, state.status);
                Assert.IsFalse(state.isPathPartial);
                Assert.AreEqual(destination, entityManager.GetComponentData<LockstepTransform>(agent).position);
                Assert.Greater(ticks, 90, "the way around the wall is longer than the straight line");
            }
        }

        [Test]
        public void Agent_FindsANewWayWhenAnObstacleBlocksIt()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager, FixedPoint.Half);
                var destination = Point(6, 0);
                var agent = CreateAgent(entityManager, Point(-6, 0), 4, destination);
                for (var i = 0; i < 15; i++)
                {
                    TestUtility.Step(simulation);
                }
                Assert.AreEqual(1, entityManager.GetBuffer<LockstepNavWaypoint>(agent).Length, "the way is straight");

                CreateWall(entityManager, FixedVector2.Zero, new FixedVector2(1, 12));
                TestUtility.Step(simulation);
                Assert.Greater(entityManager.GetBuffer<LockstepNavWaypoint>(agent).Length, 1, "the agent planned around the new wall");

                WalkUntilArrived(simulation, grid, agent, 200);
                Assert.AreEqual(LockstepNavStatus.Arrived, entityManager.GetComponentData<LockstepNavAgent>(agent).status);
                Assert.AreEqual(destination, entityManager.GetComponentData<LockstepTransform>(agent).position);
            }
        }

        [Test]
        public void Agent_OnAPartialPath_PlansAgainWhenTheWayOpens()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager);
                var walls = new[]
                {
                    CreateWall(entityManager, new FixedVector2(3, 5), new FixedVector2(FixedPoint.Half, 5)),
                    CreateWall(entityManager, new FixedVector2(7, 5), new FixedVector2(FixedPoint.Half, 5)),
                    CreateWall(entityManager, new FixedVector2(5, 3), new FixedVector2(5, FixedPoint.Half)),
                    CreateWall(entityManager, new FixedVector2(5, 7), new FixedVector2(5, FixedPoint.Half)),
                };
                var destination = Point(5, 5);
                var agent = CreateAgent(entityManager, Point(-8, -8), 4, destination);
                for (var i = 0; i < 5; i++)
                {
                    TestUtility.Step(simulation);
                }
                Assert.IsTrue(entityManager.GetComponentData<LockstepNavAgent>(agent).isPathPartial, "the destination is boxed in");

                foreach (var wall in walls)
                {
                    entityManager.DestroyEntity(wall);
                }
                WalkUntilArrived(simulation, grid, agent, 200);
                var state = entityManager.GetComponentData<LockstepNavAgent>(agent);
                Assert.IsFalse(state.isPathPartial);
                Assert.AreEqual(destination, entityManager.GetComponentData<LockstepTransform>(agent).position);
            }
        }

        [Test]
        public void Agent_WithoutAGrid_WalksStraightAndFacesItsWay()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var destination = new FixedVector3(3, 0, 4);
                var agent = CreateAgent(entityManager, FixedVector3.Zero, 5, destination);
                for (var i = 0; i < 40; i++)
                {
                    TestUtility.Step(simulation);
                }
                var transform = entityManager.GetComponentData<LockstepTransform>(agent);
                Assert.AreEqual(LockstepNavStatus.Arrived, entityManager.GetComponentData<LockstepNavAgent>(agent).status);
                Assert.AreEqual(destination, transform.position);
                var forward = transform.Forward;
                Assert.AreEqual(0.6, (double)forward.x, 1e-3);
                Assert.AreEqual(0.8, (double)forward.z, 1e-3);
            }
        }

        [Test]
        public void Session_AgentsWalkTheSamePathsOnEveryClient()
        {
            var settings = LockstepServerSettings.Default;
            settings.TickRate = 30;
            settings.MaxPlayers = 2;
            settings.InputSize = UnsafeUtility.SizeOf<TestInput>();
            settings.Seed = 77;
            settings.MinPlayersToStart = 2;
            settings.StartDelaySeconds = 0.3f;
            settings.ChecksumInterval = 10;

            using (var session = new SessionHarness(settings, latency: 0.05, jitter: 0.04))
            {
                var first = session.AddClient(SessionOptions());
                var second = session.AddClient(SessionOptions());
                session.Run(14);

                Assert.IsEmpty(session.Desyncs);
                TestUtility.AssertSameChecksums(first, second, 20);
                foreach (var client in new[] { first, second })
                {
                    Assert.Greater(client.SimulatedTicks, TestNavigationSystem.RETURN_TICK + 200);
                    var entityManager = client.Simulation.World.EntityManager;
                    using (var query = entityManager.CreateEntityQuery(typeof(LockstepNavAgent), typeof(LockstepTransform), typeof(TestNavigationTarget)))
                    {
                        var agents = query.ToComponentDataArray<LockstepNavAgent>(Allocator.Temp);
                        var transforms = query.ToComponentDataArray<LockstepTransform>(Allocator.Temp);
                        var targets = query.ToComponentDataArray<TestNavigationTarget>(Allocator.Temp);
                        Assert.AreEqual(6, agents.Length);
                        for (var i = 0; i < agents.Length; i++)
                        {
                            Assert.AreEqual(LockstepNavStatus.Arrived, agents[i].status);
                            Assert.AreEqual(targets[i].back, transforms[i].position, "every agent walked back home");
                        }
                    }
                }
            }
        }

        private static LockstepSimulationOptions SessionOptions()
        {
            var options = TestUtility.Options(typeof(LockstepNavSystemGroup), typeof(LockstepNavObstacleSystem), typeof(LockstepNavPathSystem),
                typeof(LockstepNavMoveSystem), typeof(TestNavigationSystem));
            options.Initialize = world =>
            {
                var entityManager = world.EntityManager;
                CreateGrid(entityManager, FixedPoint.FromFraction(1, 4));
                CreateWall(entityManager, new FixedVector2(-2, 3), new FixedVector2(1, 8));
                for (var i = 0; i < 6; i++)
                {
                    var z = (FixedPoint)(i * 3 - 7);
                    var agent = CreateAgent(entityManager, Point(-8, z), 3 + i, default);
                    entityManager.SetComponentData(agent, new LockstepNavAgent { speed = 3 + i, angularSpeed = FixedMath.Pi });
                    entityManager.AddComponentData(agent, new TestNavigationTarget { there = Point(8, -z), back = Point(-8, z) });
                }
            };
            return options;
        }

        private static Entity CreateGrid(EntityManager entityManager, FixedPoint agentRadius = default)
        {
            var grid = entityManager.CreateEntity(typeof(LockstepNavGrid));
            entityManager.SetComponentData(grid, CreateGrid(agentRadius));
            return grid;
        }

        // A static obstacle: a rectangle in world space.
        private static Entity CreateWall(EntityManager entityManager, FixedVector2 center, FixedVector2 size)
        {
            var wall = entityManager.CreateEntity(typeof(LockstepNavObstacle));
            entityManager.SetComponentData(wall, new LockstepNavObstacle { center = center, size = size });
            return wall;
        }

        private static Entity CreateAgent(EntityManager entityManager, FixedVector3 position, FixedPoint speed, FixedVector3 destination)
        {
            var agent = entityManager.CreateEntity(typeof(LockstepTransform), typeof(LockstepNavAgent), typeof(LockstepNavWaypoint));
            entityManager.SetComponentData(agent, LockstepTransform.FromPosition(position));
            var state = new LockstepNavAgent { speed = speed };
            if (destination != default)
            {
                state.SetDestination(destination);
            }
            entityManager.SetComponentData(agent, state);
            return agent;
        }

        // Steps until the agent arrives, checking on every tick that it stands on a walkable cell. Returns the ticks taken.
        private static int WalkUntilArrived(LockstepSimulation simulation, Entity grid, Entity agent, int maxTicks)
        {
            var entityManager = simulation.World.EntityManager;
            for (var tick = 1; tick <= maxTicks; tick++)
            {
                TestUtility.Step(simulation);
                var position = entityManager.GetComponentData<LockstepTransform>(agent).position;
                Assert.IsTrue(IsWalkable(entityManager, grid, position), $"tick {tick}: the agent stands on a blocked cell at {position}");
                if (entityManager.GetComponentData<LockstepNavAgent>(agent).status == LockstepNavStatus.Arrived)
                {
                    return tick;
                }
            }
            Assert.Fail($"the agent did not arrive in {maxTicks} ticks");
            return maxTicks;
        }

        private static bool IsWalkable(EntityManager entityManager, Entity grid, FixedVector3 position)
        {
            var cells = entityManager.GetBuffer<LockstepNavCell>(grid).AsNativeArray();
            return LockstepNavigation.IsWalkable(entityManager.GetComponentData<LockstepNavGrid>(grid), cells, position);
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct PathBatchJob : IJob
        {
            public LockstepNavGrid grid;
            [ReadOnly] public NativeArray<LockstepNavCell> cells;
            [ReadOnly] public NativeArray<FixedVector3> starts;
            [ReadOnly] public NativeArray<FixedVector3> destinations;
            public NativeList<long> results;

            public void Execute()
            {
                var pathfinder = new LockstepPathfinder(grid.CellCount, Allocator.Temp);
                var waypoints = new NativeList<FixedVector3>(Allocator.Temp);
                for (var i = 0; i < starts.Length; i++)
                {
                    results.Add((long)pathfinder.FindPath(grid, cells, starts[i], destinations[i], waypoints));
                    results.Add(waypoints.Length);
                    for (var j = 0; j < waypoints.Length; j++)
                    {
                        results.Add(waypoints[j].x.rawValue);
                        results.Add(waypoints[j].y.rawValue);
                        results.Add(waypoints[j].z.rawValue);
                    }
                }
                waypoints.Dispose();
                pathfinder.Dispose();
            }
        }
    }
}
