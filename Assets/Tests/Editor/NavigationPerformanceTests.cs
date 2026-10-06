using System;
using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using Pragma.Lockstep.Mathematics;
using Pragma.Lockstep.Navigation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Pragma.Lockstep.Tests
{
    /// <summary>
    /// How long navigation takes on a crowded map: eight armies of 150 agents on a 256 by 256 grid with buildings and a
    /// wall with gaps. Measures time rather than checks behaviour, so it runs only when picked by name.
    /// </summary>
    [TestFixture]
    [Explicit("Measures time; run it by name")]
    [Category("Performance")]
    public class NavigationPerformanceTests
    {
        private const int ARMIES = 8;
        private const int ARMY_SIZE = 150;
        private const int ROW = 15;

        private static readonly Type[] NavigationSystems =
        {
            typeof(LockstepNavSystemGroup),
            typeof(LockstepNavObstacleSystem),
            typeof(LockstepNavPathSystem),
            typeof(LockstepNavAvoidanceSystem),
            typeof(LockstepNavMoveSystem),
            typeof(LockstepNavSeparationSystem),
        };

        [Test]
        public void Navigation_EightArmiesCrossTheMap()
        {
            BurstCompiler.Options.EnableBurstCompileSynchronously = true;
            var report = new StringBuilder("NAVPERF\n");
            RunArmies(false, 300, report);
            RunArmies(true, 2400, report);
            UnityEngine.Debug.Log(report.ToString());
        }

        // Eight armies cross the map, every soldier sent alone or every army as a group; reports the time of the ticks.
        private static void RunArmies(bool isGrouped, int walkingTicks, StringBuilder report)
        {
            report.AppendLine(isGrouped ? "every army sent as a group:" : "every soldier sent alone:");
            using (var simulation = new LockstepSimulation(TestUtility.Config(checksumInterval: 0), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateMap(entityManager);
                var avoidance = entityManager.CreateEntity(typeof(LockstepNavAvoidance));
                entityManager.SetComponentData(avoidance, LockstepNavAvoidance.Default);

                var agents = new Entity[ARMIES * ARMY_SIZE];
                for (var army = 0; army < ARMIES; army++)
                {
                    for (var i = 0; i < ARMY_SIZE; i++)
                    {
                        var position = GetArmyCenter(army) + GetOffset(i);
                        var agent = entityManager.CreateEntity(typeof(LockstepTransform), typeof(LockstepNavAgent), typeof(LockstepNavWaypoint), typeof(LockstepNavVelocity));
                        entityManager.SetComponentData(agent, LockstepTransform.FromPosition(position));
                        entityManager.SetComponentData(agent, new LockstepNavAgent { speed = 4, radius = FixedPoint.FromFraction(9, 20), stoppingDistance = FixedPoint.FromFraction(1, 10) });
                        agents[army * ARMY_SIZE + i] = agent;
                    }
                }

                for (var tick = 0; tick < 5; tick++)
                {
                    Step(simulation);
                }
                report.AppendLine($"  idle tick: {Measure(simulation, 30, out _):F2} ms average");

                var starts = new FixedVector3[agents.Length];
                var destinations = new FixedVector3[agents.Length];
                for (var army = 0; army < ARMIES; army++)
                {
                    var target = GetArmyCenter((army + ARMIES / 2) % ARMIES);
                    target = new FixedVector3(-target.x, target.y, target.z);
                    for (var i = 0; i < ARMY_SIZE; i++)
                    {
                        var index = army * ARMY_SIZE + i;
                        var agent = entityManager.GetComponentData<LockstepNavAgent>(agents[index]);
                        if (isGrouped)
                        {
                            agent.SetDestination(target + GetOffset(i), target);
                        }
                        else
                        {
                            agent.SetDestination(target + GetOffset(i));
                        }
                        entityManager.SetComponentData(agents[index], agent);
                        starts[index] = entityManager.GetComponentData<LockstepTransform>(agents[index]).position;
                        destinations[index] = agent.destination;
                    }
                }
                if (!isGrouped)
                {
                    report.AppendLine($"  A* alone, one thread: {MeasurePaths(entityManager, grid, starts, destinations, out var corners):F3} ms per path, {corners:F1} waypoints");
                }
                else
                {
                    MeasureGroup(entityManager, grid, starts, destinations, out var search, out var follow);
                    report.AppendLine($"  one army, one thread: {search:F3} ms the search, {follow:F3} ms per path along it");
                }
                report.AppendLine($"  order tick ({agents.Length} agents plan): {Measure(simulation, 1, out _):F2} ms");
                report.AppendLine($"  next tick: {Measure(simulation, 1, out _):F2} ms");
                report.AppendLine($"  walking ticks: {Measure(simulation, walkingTicks, out var slowest):F2} ms average, {slowest:F2} ms slowest");

                var arrived = 0;
                var partial = 0;
                var farthest = 0.0;
                foreach (var agent in agents)
                {
                    var state = entityManager.GetComponentData<LockstepNavAgent>(agent);
                    if (state.status == LockstepNavStatus.Arrived)
                    {
                        arrived++;
                        continue;
                    }
                    partial += state.isPathPartial ? 1 : 0;
                    farthest = Math.Max(farthest, (double)FixedMath.Distance(entityManager.GetComponentData<LockstepTransform>(agent).position.Xz, state.destination.Xz));
                }
                report.AppendLine($"  arrived after {walkingTicks / 30} s: {arrived} of {agents.Length}; of the rest {partial} on partial paths, the farthest {farthest:F1} m from its place");
            }
        }

        // A 256 by 256 grid of 1 m cells with forty buildings and a wall across the middle with four gaps.
        private static Entity CreateMap(EntityManager entityManager)
        {
            var grid = entityManager.CreateEntity(typeof(LockstepNavGrid));
            entityManager.SetComponentData(grid, new LockstepNavGrid
            {
                origin = new FixedVector2(-128, -128),
                cellSize = FixedPoint.One,
                width = 256,
                height = 256,
                agentRadius = FixedPoint.Half,
            });

            for (var x = -110; x < 110; x += 40)
            {
                // Wall segments 34 m long with 6 m gaps between them.
                CreateObstacle(entityManager, new FixedVector2(x + 17, 0), new FixedVector2(34, 2));
            }

            var random = 12345u;
            for (var i = 0; i < 40; i++)
            {
                var x = (int)(Next(ref random) % 180) - 90;
                var z = 20 + (int)(Next(ref random) % 50);
                CreateObstacle(entityManager, new FixedVector2(x, i % 2 == 0 ? z : -z), new FixedVector2(8, 6));
            }
            return grid;
        }

        private static uint Next(ref uint state)
        {
            state = state * 1664525u + 1013904223u;
            return state >> 8;
        }

        private static void CreateObstacle(EntityManager entityManager, FixedVector2 center, FixedVector2 size)
        {
            var obstacle = entityManager.CreateEntity(typeof(LockstepNavObstacle));
            entityManager.SetComponentData(obstacle, new LockstepNavObstacle { center = center, size = size });
        }

        // Four armies in the south and four in the north.
        private static FixedVector3 GetArmyCenter(int army)
        {
            var x = -90 + army % 4 * 60;
            var z = army < 4 ? -100 : 100;
            return new FixedVector3(x, 0, z);
        }

        // Rows of fifteen agents, 1.2 m apart.
        private static FixedVector3 GetOffset(int index)
        {
            var spacing = FixedPoint.FromFraction(6, 5);
            return new FixedVector3(spacing * (index % ROW - ROW / 2), 0, spacing * (index / ROW - 5));
        }

        private static void Step(LockstepSimulation simulation)
        {
            TestUtility.Step(simulation);
            simulation.World.EntityManager.CompleteAllTrackedJobs();
        }

        // Average milliseconds per tick over the ticks, and the slowest one.
        private static double Measure(LockstepSimulation simulation, int ticks, out double slowest)
        {
            slowest = 0;
            var total = 0.0;
            var watch = new Stopwatch();
            for (var tick = 0; tick < ticks; tick++)
            {
                watch.Restart();
                Step(simulation);
                var elapsed = watch.Elapsed.TotalMilliseconds;
                total += elapsed;
                slowest = Math.Max(slowest, elapsed);
            }
            return total / ticks;
        }

        // Milliseconds per path and waypoints per path, planning the paths one after another on one thread.
        private static double MeasurePaths(EntityManager entityManager, Entity grid, FixedVector3[] starts, FixedVector3[] destinations, out double corners)
        {
            var job = new PathsJob
            {
                grid = entityManager.GetComponentData<LockstepNavGrid>(grid),
                cells = entityManager.GetBuffer<LockstepNavCell>(grid).AsNativeArray(),
                starts = new NativeArray<FixedVector3>(starts, Allocator.TempJob),
                destinations = new NativeArray<FixedVector3>(destinations, Allocator.TempJob),
                waypoints = new NativeArray<int>(1, Allocator.TempJob),
            };
            job.Run();
            var watch = Stopwatch.StartNew();
            job.Run();
            var elapsed = watch.Elapsed.TotalMilliseconds;
            corners = (double)job.waypoints[0] / starts.Length;
            job.starts.Dispose();
            job.destinations.Dispose();
            job.waypoints.Dispose();
            return elapsed / starts.Length;
        }

        // Milliseconds of the search of the first army, and per path of a soldier along its chain, on one thread.
        private static void MeasureGroup(EntityManager entityManager, Entity grid, FixedVector3[] starts, FixedVector3[] destinations, out double search, out double follow)
        {
            var job = new GroupJob
            {
                grid = entityManager.GetComponentData<LockstepNavGrid>(grid),
                cells = entityManager.GetBuffer<LockstepNavCell>(grid).AsNativeArray(),
                starts = new NativeArray<FixedVector3>(ARMY_SIZE, Allocator.TempJob),
                destinations = new NativeArray<FixedVector3>(ARMY_SIZE, Allocator.TempJob),
                goal = GetArmyCenter(0),
                isFollowing = false,
            };
            var target = GetArmyCenter(ARMIES / 2);
            job.goal = new FixedVector3(-target.x, target.y, target.z);
            for (var i = 0; i < ARMY_SIZE; i++)
            {
                job.starts[i] = starts[i];
                job.destinations[i] = destinations[i];
            }
            job.Run();
            var watch = Stopwatch.StartNew();
            job.Run();
            search = watch.Elapsed.TotalMilliseconds;
            job.isFollowing = true;
            job.Run();
            watch.Restart();
            job.Run();
            follow = (watch.Elapsed.TotalMilliseconds - search) / ARMY_SIZE;
            job.starts.Dispose();
            job.destinations.Dispose();
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct GroupJob : IJob
        {
            public LockstepNavGrid grid;
            [ReadOnly] public NativeArray<LockstepNavCell> cells;
            [ReadOnly] public NativeArray<FixedVector3> starts;
            [ReadOnly] public NativeArray<FixedVector3> destinations;
            public FixedVector3 goal;
            public bool isFollowing;

            public void Execute()
            {
                var map = new LockstepNavPathMap(grid, cells);
                var pathfinder = new LockstepPathfinder(grid.CellCount, Allocator.Temp);
                var cellsOfStarts = new NativeArray<int>(starts.Length, Allocator.Temp);
                for (var i = 0; i < starts.Length; i++)
                {
                    LockstepPathfinder.TryGetStartCell(map, starts[i], out var cell);
                    cellsOfStarts[i] = grid.GetIndex(cell);
                }
                LockstepPathfinder.TryGetEndCell(map, goal, out var goalCell, out _);
                pathfinder.SearchGroup(map, grid.GetIndex(goalCell), cellsOfStarts);
                if (isFollowing)
                {
                    var chain = new NativeList<int>(Allocator.Temp);
                    var path = new NativeList<FixedVector3>(Allocator.Temp);
                    for (var i = 0; i < starts.Length; i++)
                    {
                        pathfinder.GetChain(cellsOfStarts[i], chain);
                        pathfinder.FollowChain(map, starts[i], destinations[i], chain.AsArray(), path);
                    }
                }
                pathfinder.Dispose();
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct PathsJob : IJob
        {
            public LockstepNavGrid grid;
            [ReadOnly] public NativeArray<LockstepNavCell> cells;
            [ReadOnly] public NativeArray<FixedVector3> starts;
            [ReadOnly] public NativeArray<FixedVector3> destinations;
            public NativeArray<int> waypoints;

            public void Execute()
            {
                var pathfinder = new LockstepPathfinder(grid.CellCount, Allocator.Temp);
                var path = new NativeList<FixedVector3>(Allocator.Temp);
                var count = 0;
                for (var i = 0; i < starts.Length; i++)
                {
                    pathfinder.FindPath(grid, cells, starts[i], destinations[i], path);
                    count += path.Length;
                }
                waypoints[0] = count;
                pathfinder.Dispose();
            }
        }
    }
}
