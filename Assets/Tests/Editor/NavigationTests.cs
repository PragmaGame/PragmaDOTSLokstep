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
            typeof(LockstepNavAvoidanceSystem),
            typeof(LockstepNavMoveSystem),
            typeof(LockstepNavSeparationSystem),
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
        public void Covers_IsWhatAStampBlocks()
        {
            var grid = CreateGrid(FixedPoint.FromFraction(1, 2));
            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
            {
                var obstacle = new LockstepNavObstacle { center = new FixedVector2(1, 0), size = new FixedVector2(3, 1) };
                var transform = LockstepTransform.FromPositionRotation(new FixedVector3(-2, 0, 4), FixedQuaternion.RotateY(FixedMath.ToRadians(55)));
                var footprint = LockstepNavObstacleFootprint.Create(obstacle, transform);
                LockstepNavigation.Stamp(grid, cells, footprint, 1);
                LockstepNavigation.GetCoverage(grid, footprint, out var min, out var max);

                var covered = 0;
                for (var i = 0; i < cells.Length; i++)
                {
                    var cell = grid.GetCell(i);
                    var isCovered = LockstepNavigation.Covers(grid, footprint, cell);
                    Assert.AreEqual(cells[i].blockers > 0, isCovered, $"cell {cell}");
                    if (isCovered)
                    {
                        Assert.IsTrue(cell.x >= min.x && cell.y >= min.y && cell.x <= max.x && cell.y <= max.y, $"cell {cell} lies in the coverage");
                        covered++;
                    }
                }
                Assert.Greater(covered, 12);
            }
        }

        [Test]
        public void IsClear_OnlyWhereAStampWouldTouchNoBlockedCell()
        {
            var grid = CreateGrid(FixedPoint.FromFraction(1, 2));
            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
            {
                var building = new LockstepNavObstacle { size = new FixedVector2(2, 2) };
                LockstepNavigation.Stamp(grid, cells, LockstepNavObstacleFootprint.Create(building, LockstepTransform.FromPosition(Point(0, 0))), 1);

                Assert.IsTrue(LockstepNavigation.IsClear(grid, cells, LockstepNavObstacleFootprint.Create(building, LockstepTransform.FromPosition(Point(5, 0)))),
                              "far enough: the agent radius of both still leaves a lane");
                Assert.IsFalse(LockstepNavigation.IsClear(grid, cells, LockstepNavObstacleFootprint.Create(building, LockstepTransform.FromPosition(Point(2, 0)))),
                               "touching: the grown footprints overlap");
                Assert.IsFalse(LockstepNavigation.IsClear(grid, cells, LockstepNavObstacleFootprint.Create(building, LockstepTransform.FromPosition(Point(0, 0)))),
                               "on top");
                Assert.IsFalse(LockstepNavigation.IsClear(grid, cells, LockstepNavObstacleFootprint.Create(building, LockstepTransform.FromPosition(Point(-9, 6)))),
                               "partly outside the grid");
                Assert.IsFalse(LockstepNavigation.IsClear(grid, new NativeArray<LockstepNavCell>(0, Allocator.Temp),
                                                          LockstepNavObstacleFootprint.Create(building, LockstepTransform.FromPosition(Point(5, 0)))),
                               "cells that do not match the grid");
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
        public void Heights_AreBilinearInsideACellAndTakeTheEdgeOutsideTheGrid()
        {
            var grid = CreateGrid();
            var heights = new NativeArray<LockstepNavHeight>(grid.CornerCount, Allocator.Temp);
            // A plane: world Y = (x + 10) / 2 + (z + 10) / 4, which bilinear interpolation reproduces.
            for (var z = 0; z <= grid.height; z++)
            {
                for (var x = 0; x <= grid.width; x++)
                {
                    heights[grid.GetCornerIndex(new int2(x, z))] = new LockstepNavHeight { value = (FixedPoint)x / 4 + (FixedPoint)z / 8 };
                }
            }

            Assert.IsTrue(LockstepNavigation.HasHeights(grid, heights));
            Assert.IsFalse(LockstepNavigation.HasHeights(grid, heights.GetSubArray(0, grid.CellCount)));
            var inside = new FixedVector2(FixedPoint.FromFraction(13, 10), FixedPoint.FromFraction(-27, 10));
            Assert.AreEqual(11.3 / 2 + 7.3 / 4, (double)LockstepNavigation.GetHeight(grid, heights, inside), 1e-3);
            Assert.AreEqual((double)LockstepNavigation.GetHeight(grid, heights, new FixedVector2(10, 2)),
                (double)LockstepNavigation.GetHeight(grid, heights, new FixedVector2(25, 2)), 1e-9, "beyond the grid the ground of its edge");
            var grounded = LockstepNavigation.ToGround(grid, heights, new FixedVector3(FixedPoint.FromFraction(13, 10), 50, FixedPoint.FromFraction(-27, 10)));
            Assert.AreEqual(LockstepNavigation.GetHeight(grid, heights, inside), grounded.y);
            Assert.AreEqual(new FixedVector3(1, 7, 2), LockstepNavigation.ToGround(grid, default, new FixedVector3(1, 7, 2)), "without heights the Y stays");
        }

        [Test]
        public void SteepGround_BlocksItsCellsForGood()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager);
                var state = entityManager.GetComponentData<LockstepNavGrid>(grid);
                state.maxSlope = FixedPoint.One;
                entityManager.SetComponentData(grid, state);
                // Corner columns 0-20 are at 0 and 21-40 at 2: the column of cells 20 between them rises 2 over half a unit.
                // Corner columns 30-40 rise by a quarter more per column: a gentle slope.
                var heights = entityManager.AddBuffer<LockstepNavHeight>(grid);
                for (var z = 0; z <= state.height; z++)
                {
                    for (var x = 0; x <= state.width; x++)
                    {
                        var height = x <= 20 ? FixedPoint.Zero : 2 + (FixedPoint)math.max(0, x - 30) / 4;
                        heights.Add(new LockstepNavHeight { value = height });
                    }
                }
                var wall = CreateWall(entityManager, new FixedVector2(0, 0), new FixedVector2(2, 2));
                TestUtility.Step(simulation);
                entityManager.DestroyEntity(wall);
                TestUtility.Step(simulation);

                var cells = entityManager.GetBuffer<LockstepNavCell>(grid);
                for (var i = 0; i < cells.Length; i++)
                {
                    var cell = state.GetCell(i);
                    Assert.AreEqual(cell.x == 20, !cells[i].IsWalkable, $"cell {cell}: only the cliff is blocked, the released wall is not");
                }
            }
        }

        [Test]
        public void Agent_WalksOverAHillOnTheGround()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager);
                var state = entityManager.GetComponentData<LockstepNavGrid>(grid);
                // A ridge along Z, 3 units high at X = 0, sloping one to one down to the plain.
                var buffer = entityManager.AddBuffer<LockstepNavHeight>(grid);
                for (var z = 0; z <= state.height; z++)
                {
                    for (var x = 0; x <= state.width; x++)
                    {
                        var worldX = state.origin.x + state.cellSize * x;
                        buffer.Add(new LockstepNavHeight { value = FixedMath.Max(FixedPoint.Zero, 3 - FixedMath.Abs(worldX)) });
                    }
                }
                var destination = new FixedVector3(6, 5, 0);
                var agent = CreateAgent(entityManager, Point(-6, 0), 4, destination);

                var top = FixedPoint.Zero;
                for (var tick = 0; tick < 200 && entityManager.GetComponentData<LockstepNavAgent>(agent).status != LockstepNavStatus.Arrived; tick++)
                {
                    TestUtility.Step(simulation);
                    var position = Position(entityManager, agent);
                    var heights = entityManager.GetBuffer<LockstepNavHeight>(grid).AsNativeArray();
                    Assert.AreEqual(LockstepNavigation.GetHeight(state, heights, position.Xz), position.y, $"tick {tick}: the agent stands on the ground");
                    top = FixedMath.Max(top, position.y);
                }

                Assert.AreEqual(LockstepNavStatus.Arrived, entityManager.GetComponentData<LockstepNavAgent>(agent).status);
                Assert.AreEqual(new FixedVector3(6, 0, 0), Position(entityManager, agent), "it arrives on the ground, not at the height of its destination");
                Assert.Greater((double)top, 2.5, "it climbed the ridge");
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
        public void Agent_StandingWhereAnObstacleAppears_WalksOutOfIt()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager, FixedPoint.Half);
                var inside = CreateAgent(entityManager, Point(FixedPoint.FromFraction(1, 4), FixedPoint.FromFraction(1, 4)), 4, default);
                var outside = CreateAgent(entityManager, Point(6, 6), 4, default);
                TestUtility.Step(simulation);
                Assert.AreEqual(LockstepNavStatus.Idle, entityManager.GetComponentData<LockstepNavAgent>(inside).status, "nothing blocks it yet");

                CreateWall(entityManager, FixedVector2.Zero, new FixedVector2(3, 3));
                TestUtility.Step(simulation);
                Assert.AreEqual(LockstepNavStatus.Moving, entityManager.GetComponentData<LockstepNavAgent>(inside).status, "the agent walks out");

                for (var i = 0; i < 60; i++)
                {
                    TestUtility.Step(simulation);
                }
                Assert.AreEqual(LockstepNavStatus.Arrived, entityManager.GetComponentData<LockstepNavAgent>(inside).status);
                Assert.IsTrue(IsWalkable(entityManager, grid, entityManager.GetComponentData<LockstepTransform>(inside).position));
                Assert.AreEqual(LockstepNavStatus.Idle, entityManager.GetComponentData<LockstepNavAgent>(outside).status, "an agent on a walkable cell stays");
                Assert.AreEqual(Point(6, 6), entityManager.GetComponentData<LockstepTransform>(outside).position);
            }
        }

        [Test]
        public void Agent_StoppedInsideAnObstacle_WalksOutOfIt()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager, FixedPoint.Half);
                CreateWall(entityManager, FixedVector2.Zero, new FixedVector2(3, 3));
                var agent = CreateAgent(entityManager, Point(-6, 0), 4, Point(6, 0));
                TestUtility.Step(simulation);

                // The agent is put inside the obstacle and stopped there, while the grid stays as it is.
                entityManager.SetComponentData(agent, LockstepTransform.FromPosition(Point(0, 0)));
                var state = entityManager.GetComponentData<LockstepNavAgent>(agent);
                state.Stop();
                entityManager.SetComponentData(agent, state);
                for (var i = 0; i < 60; i++)
                {
                    TestUtility.Step(simulation);
                }
                Assert.AreEqual(LockstepNavStatus.Arrived, entityManager.GetComponentData<LockstepNavAgent>(agent).status);
                Assert.IsTrue(IsWalkable(entityManager, grid, entityManager.GetComponentData<LockstepTransform>(agent).position));
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
        public void Agents_OnTheSamePoint_SpreadUntilTheyStopOverlapping()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager);
                var start = Point(1, 1);
                var agents = new Entity[5];
                for (var i = 0; i < agents.Length; i++)
                {
                    agents[i] = CreateAgent(entityManager, start, 4, default, FixedPoint.Half);
                }

                for (var i = 0; i < 60; i++)
                {
                    TestUtility.Step(simulation);
                }
                for (var i = 0; i < agents.Length; i++)
                {
                    var position = Position(entityManager, agents[i]);
                    Assert.IsTrue(IsWalkable(entityManager, grid, position));
                    Assert.Less((double)FixedMath.Distance(position, start), 2.0, "the crowd spreads around where it stood");
                    for (var j = 0; j < i; j++)
                    {
                        var distance = FixedMath.Distance(position, Position(entityManager, agents[j]));
                        Assert.Greater((double)distance, 0.95, $"agents {j} and {i} still overlap");
                    }
                }
            }
        }

        [Test]
        public void Agents_AreNotPushedOntoBlockedCells()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                // Bodies as large as the agent radius of the grid: the cells next to the wall fit them.
                var grid = CreateGrid(entityManager, FixedPoint.Half);
                CreateWall(entityManager, FixedVector2.Zero, new FixedVector2(1, 12));
                // The right agent stands at the wall: its push goes into it, so only the left one gives way.
                var left = CreateAgent(entityManager, Point(FixedPoint.FromFraction(-5, 4), 0), 4, default, FixedPoint.Half);
                var right = CreateAgent(entityManager, Point(FixedPoint.FromFraction(-21, 20), 0), 4, default, FixedPoint.Half);

                for (var tick = 1; tick <= 40; tick++)
                {
                    TestUtility.Step(simulation);
                    Assert.IsTrue(IsWalkable(entityManager, grid, Position(entityManager, left)), $"tick {tick}: left agent on a blocked cell");
                    Assert.IsTrue(IsWalkable(entityManager, grid, Position(entityManager, right)), $"tick {tick}: right agent on a blocked cell");
                }
                // Pushes small enough to keep it on its cell bring it up to the wall, never away from it.
                Assert.GreaterOrEqual((double)Position(entityManager, right).x, -1.05, "the agent at the wall does not give way");
                Assert.Greater((double)FixedMath.Distance(Position(entityManager, left), Position(entityManager, right)), 0.95);
            }
        }

        [Test]
        public void Agent_Walking_TakesThreeQuartersOfThePushFromAStandingOne()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                CreateGrid(entityManager);
                var stander = CreateAgent(entityManager, Point(0, 0), 4, default, FixedPoint.Half);
                // The walker overlaps the stander from the side and walks away along Z.
                var start = Point(-FixedPoint.Half, 0);
                var walker = CreateAgent(entityManager, start, 4, Point(-FixedPoint.Half, 8), FixedPoint.Half);

                TestUtility.Step(simulation);
                var standerShift = (double)Position(entityManager, stander).x;
                var walkerShift = (double)(start.x - Position(entityManager, walker).x);
                Assert.Greater(standerShift, 0.0, "the standing agent is pushed a little");
                Assert.AreEqual(3.0, walkerShift / standerShift, 0.01, "the walking agent gives way three times as much");
            }
        }

        [Test]
        public void Agent_WalkingToWhereAnotherStands_ShouldersItAsideAndArrives()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager);
                var destination = Point(2, 0);
                var stander = CreateAgent(entityManager, destination, 4, default, FixedPoint.Half);
                var walker = CreateAgent(entityManager, Point(-2, 0), 4, destination, FixedPoint.Half);

                WalkUntilArrived(simulation, grid, walker, 200);
                Assert.Greater((double)FixedMath.Distance(Position(entityManager, stander), destination), 0.5, "the standing agent made room");
            }
        }

        [Test]
        public void Agent_WithoutARadius_NeitherPushesNorIsPushed()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                CreateGrid(entityManager);
                var body = CreateAgent(entityManager, Point(2, 2), 4, default, FixedPoint.Half);
                var ghost = CreateAgent(entityManager, Point(2, 2), 4, default);

                for (var i = 0; i < 10; i++)
                {
                    TestUtility.Step(simulation);
                }
                Assert.AreEqual(Point(2, 2), Position(entityManager, body));
                Assert.AreEqual(Point(2, 2), Position(entityManager, ghost));
            }
        }

        [Test]
        public void Separation_DoesNotDependOnTheOrderOfAgents()
        {
            var starts = new[]
            {
                Point(0, 0),
                Point(FixedPoint.FromFraction(3, 10), FixedPoint.FromFraction(1, 10)),
                Point(FixedPoint.FromFraction(-1, 5), FixedPoint.FromFraction(2, 5)),
                Point(FixedPoint.Half, FixedPoint.FromFraction(-3, 10)),
                Point(FixedPoint.FromFraction(1, 10), FixedPoint.FromFraction(-3, 5)),
                Point(FixedPoint.FromFraction(-7, 10), FixedPoint.FromFraction(-1, 10)),
            };
            var forward = SeparateFrom(starts, false);
            var backward = SeparateFrom(starts, true);
            for (var i = 0; i < starts.Length; i++)
            {
                Assert.AreNotEqual(starts[i], forward[i], $"agent {i} was pushed");
                Assert.AreEqual(forward[i], backward[i], $"agent {i} ends on the same point whatever the order");
            }
        }

        // Creates agents of radius 1/2 on the starts (in reverse order if asked), separates them for 20 ticks and returns
        // their positions in the order of the starts.
        private static FixedVector3[] SeparateFrom(FixedVector3[] starts, bool reverse)
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                CreateGrid(entityManager);
                var agents = new Entity[starts.Length];
                for (var k = 0; k < starts.Length; k++)
                {
                    var i = reverse ? starts.Length - 1 - k : k;
                    agents[i] = CreateAgent(entityManager, starts[i], 4, default, FixedPoint.Half);
                }
                for (var tick = 0; tick < 20; tick++)
                {
                    TestUtility.Step(simulation);
                }
                var positions = new FixedVector3[starts.Length];
                for (var i = 0; i < starts.Length; i++)
                {
                    positions[i] = Position(entityManager, agents[i]);
                }
                return positions;
            }
        }

        [Test]
        public void Avoidance_AgentsWalkingIntoEachOther_PassWithoutTouching()
        {
            var avoiding = WalkHeadOn(true);
            var plain = WalkHeadOn(false);
            Assert.Greater(avoiding, 0.95, "with avoidance the bodies do not touch");
            Assert.Less(plain, 0.9, "without it they walk into each other");
        }

        // Two agents of radius 1/2 walk at each other along X, each to where the other started. Returns the least
        // distance between them; with avoidance both must arrive (without it they push each other along their line and
        // get stuck).
        private static double WalkHeadOn(bool isAvoiding)
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager);
                if (isAvoiding)
                {
                    CreateAvoidance(entityManager);
                }
                var left = WithVelocity(entityManager, CreateAgent(entityManager, Point(-6, 0), 4, Point(6, 0), FixedPoint.Half));
                var right = WithVelocity(entityManager, CreateAgent(entityManager, Point(6, 0), 4, Point(-6, 0), FixedPoint.Half));

                var least = double.MaxValue;
                for (var tick = 1; tick <= 200 && !(HasArrived(entityManager, left) && HasArrived(entityManager, right)); tick++)
                {
                    TestUtility.Step(simulation);
                    Assert.IsTrue(IsWalkable(entityManager, grid, Position(entityManager, left)) && IsWalkable(entityManager, grid, Position(entityManager, right)));
                    least = Math.Min(least, (double)FixedMath.Distance(Position(entityManager, left), Position(entityManager, right)));
                }
                if (isAvoiding)
                {
                    Assert.IsTrue(HasArrived(entityManager, left) && HasArrived(entityManager, right), "both agents arrived");
                    Assert.AreEqual(Point(6, 0), Position(entityManager, left));
                    Assert.AreEqual(Point(-6, 0), Position(entityManager, right));
                }
                return least;
            }
        }

        [Test]
        public void Avoidance_WalkerGoesAroundAStandingAgent()
        {
            var avoiding = PassStander(true);
            var plain = PassStander(false);
            Assert.Less(avoiding, 0.02, "with avoidance the walker goes around the standing agent");
            Assert.Greater(plain, 0.1, "without it the walker shoulders the standing agent aside");
        }

        // A walker passes a standing agent a little off its way along X. Returns how far the standing agent was pushed.
        private static double PassStander(bool isAvoiding)
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager, FixedPoint.Half);
                if (isAvoiding)
                {
                    CreateAvoidance(entityManager);
                }
                var start = Point(0, FixedPoint.FromFraction(1, 5));
                var stander = WithVelocity(entityManager, CreateAgent(entityManager, start, 4, default, FixedPoint.Half));
                var walker = WithVelocity(entityManager, CreateAgent(entityManager, Point(-5, 0), 4, Point(5, 0), FixedPoint.Half));

                WalkUntilArrived(simulation, grid, walker, 200);
                Assert.AreEqual(Point(5, 0), Position(entityManager, walker), "the walker arrived where it was sent");
                return (double)FixedMath.Distance(Position(entityManager, stander), start);
            }
        }

        [Test]
        public void Avoidance_StandingAgentAtTheDestination_IsShoulderedAside()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager);
                CreateAvoidance(entityManager);
                var destination = Point(2, 0);
                var stander = WithVelocity(entityManager, CreateAgent(entityManager, destination, 4, default, FixedPoint.Half));
                var walker = WithVelocity(entityManager, CreateAgent(entityManager, Point(-2, 0), 4, destination, FixedPoint.Half));

                WalkUntilArrived(simulation, grid, walker, 200);
                Assert.Greater((double)FixedMath.Distance(Position(entityManager, stander), destination), 0.5, "the standing agent made room");
            }
        }

        [Test]
        public void Avoidance_NeverStepsOntoBlockedCells()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager, FixedPoint.FromFraction(1, 4));
                CreateAvoidance(entityManager);
                // A corridor two units wide between two walls: the agents meet in it and have little room to turn.
                CreateWall(entityManager, new FixedVector2(0, FixedPoint.FromFraction(3, 2)), new FixedVector2(10, 1));
                CreateWall(entityManager, new FixedVector2(0, FixedPoint.FromFraction(-3, 2)), new FixedVector2(10, 1));
                var agents = new[]
                {
                    WithVelocity(entityManager, CreateAgent(entityManager, Point(-7, 0), 4, Point(7, 0), FixedPoint.FromFraction(2, 5))),
                    WithVelocity(entityManager, CreateAgent(entityManager, Point(7, FixedPoint.FromFraction(1, 4)), 3, Point(-7, 0), FixedPoint.FromFraction(2, 5))),
                    WithVelocity(entityManager, CreateAgent(entityManager, Point(-8, FixedPoint.FromFraction(1, 2)), 5, Point(7, FixedPoint.FromFraction(1, 2)), FixedPoint.FromFraction(2, 5))),
                };

                for (var tick = 1; tick <= 400 && !AllArrived(entityManager, agents); tick++)
                {
                    TestUtility.Step(simulation);
                    foreach (var agent in agents)
                    {
                        Assert.IsTrue(IsWalkable(entityManager, grid, Position(entityManager, agent)), $"tick {tick}: an agent stands on a blocked cell at {Position(entityManager, agent)}");
                    }
                }
                Assert.IsTrue(AllArrived(entityManager, agents), "the agents made their way through the corridor");
            }
        }

        [Test]
        public void Avoidance_Off_WalksExactlyLikeWithoutAVelocity()
        {
            using (var plain = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            using (var recorded = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var plainManager = plain.World.EntityManager;
                var recordedManager = recorded.World.EntityManager;
                CreateGrid(plainManager);
                CreateGrid(recordedManager);
                CreateWall(plainManager, new FixedVector2(0, 1), new FixedVector2(1, 6));
                CreateWall(recordedManager, new FixedVector2(0, 1), new FixedVector2(1, 6));
                var first = CreateAgent(plainManager, Point(-4, 1), 3, Point(4, 2), FixedPoint.Half);
                var second = WithVelocity(recordedManager, CreateAgent(recordedManager, Point(-4, 1), 3, Point(4, 2), FixedPoint.Half));

                for (var tick = 1; tick <= 120; tick++)
                {
                    var before = Position(recordedManager, second);
                    TestUtility.Step(plain);
                    TestUtility.Step(recorded);
                    var after = Position(recordedManager, second);
                    var deltaTime = recordedManager.CreateEntityQuery(typeof(LockstepTime)).GetSingleton<LockstepTime>().deltaTime;
                    Assert.AreEqual(Position(plainManager, first), after, $"tick {tick}: the same step");
                    Assert.AreEqual((after - before).Xz / deltaTime, recordedManager.GetComponentData<LockstepNavVelocity>(second).value, $"tick {tick}: the velocity is the step");
                }
                Assert.IsTrue(HasArrived(recordedManager, second));
            }
        }

        [Test]
        public void Avoidance_TwoGroupsCrossing_ArriveWithLessOverlap()
        {
            var avoiding = CrossGroups(true);
            var plain = CrossGroups(false);
            Assert.Less(avoiding, plain / 2, "avoidance halves how deep the agents walk into each other");
        }

        // Two lines of five agents walk through each other at right angles. Returns the sum over the ticks of the
        // deepest overlap between two agents; with avoidance all must arrive.
        private static double CrossGroups(bool isAvoiding)
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager);
                if (isAvoiding)
                {
                    CreateAvoidance(entityManager);
                }
                var radius = FixedPoint.FromFraction(2, 5);
                var agents = new Entity[10];
                for (var i = 0; i < 5; i++)
                {
                    var offset = (FixedPoint)(i - 2) * FixedPoint.FromFraction(6, 5);
                    agents[i] = WithVelocity(entityManager, CreateAgent(entityManager, Point(-8, offset), 4, Point(8, offset), radius));
                    agents[i + 5] = WithVelocity(entityManager, CreateAgent(entityManager, Point(offset, -8), 4, Point(offset, 8), radius));
                }

                var overlap = 0.0;
                for (var tick = 1; tick <= 400 && !AllArrived(entityManager, agents); tick++)
                {
                    TestUtility.Step(simulation);
                    var deepest = 0.0;
                    for (var i = 0; i < agents.Length; i++)
                    {
                        Assert.IsTrue(IsWalkable(entityManager, grid, Position(entityManager, agents[i])));
                        for (var j = 0; j < i; j++)
                        {
                            var distance = (double)FixedMath.Distance(Position(entityManager, agents[i]), Position(entityManager, agents[j]));
                            deepest = Math.Max(deepest, 2 * (double)radius - distance);
                        }
                    }
                    overlap += deepest;
                }
                Assert.IsTrue(!isAvoiding || AllArrived(entityManager, agents), "every agent got through");
                return overlap;
            }
        }

        [Test]
        public void Clearance_CountsRingsToTheNearestBlockedCellOrEdge()
        {
            var grid = CreateGrid();
            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
            {
                // Cells (19, 19) to (20, 20): the square of 1 by 1 around the origin.
                Block(grid, cells, FixedVector2.Zero, new FixedVector2(1, 1));
                LockstepNavigation.UpdateClearance(grid, cells);

                Assert.AreEqual(0, Clearance(grid, cells, 20, 20), "a blocked cell");
                Assert.AreEqual(1, Clearance(grid, cells, 21, 20), "next to a blocked cell");
                Assert.AreEqual(2, Clearance(grid, cells, 22, 21));
                Assert.AreEqual(3, Clearance(grid, cells, 23, 23), "diagonal rings count like straight ones");
                Assert.AreEqual(1, Clearance(grid, cells, 0, 7), "on the edge of the grid");
                Assert.AreEqual(6, Clearance(grid, cells, 5, 5), "the edge is nearer than the block");
            }
        }

        private static int Clearance(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, int x, int y)
        {
            return cells[grid.GetIndex(new int2(x, y))].clearance;
        }

        [Test]
        public void Clearance_OfABody_GrowsWithItsRadiusBeyondTheAgentRadius()
        {
            // Cells of half a unit.
            var grid = CreateGrid(FixedPoint.FromFraction(1, 4));
            Assert.AreEqual(1, LockstepNavigation.GetClearance(grid, FixedPoint.Zero));
            Assert.AreEqual(1, LockstepNavigation.GetClearance(grid, FixedPoint.FromFraction(1, 4)), "as large as the agent radius: any walkable cell");
            Assert.AreEqual(1, LockstepNavigation.GetClearance(grid, FixedPoint.FromFraction(9, 20)), "less than half a cell larger: still any walkable cell");
            Assert.AreEqual(2, LockstepNavigation.GetClearance(grid, FixedPoint.FromFraction(3, 4)), "a cell larger: one ring more");
            Assert.AreEqual(3, LockstepNavigation.GetClearance(grid, 1), "a cell and a half larger: two more");
        }

        [Test]
        public void LargeAgent_GoesAroundThroughAGapItsBodyFits()
        {
            var small = CrossWallWithGaps(FixedPoint.FromFraction(1, 4), out var isSmallClear);
            var large = CrossWallWithGaps(1, out var isLargeClear);
            Assert.Less(small, 1.0, "a small agent takes the narrow gap on its way");
            Assert.That(large, Is.InRange(3.0, 8.0), "a large one goes round through the wide gap");
            Assert.IsTrue(isSmallClear && isLargeClear, "neither ever stood where its body does not fit");
        }

        // An agent of the radius walks from (-6, 0) to (6, 0) through a wall along Z at X 0 with a gap 1.5 units wide at Z 0
        // and one 5 units wide between Z 3 and 8. Returns how far from Z 0 it crossed the wall; checks every tick that it
        // stood on a cell its body fits in.
        private static double CrossWallWithGaps(FixedPoint radius, out bool isClear)
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager, FixedPoint.FromFraction(1, 4));
                CreateWall(entityManager, new FixedVector2(0, FixedPoint.FromFraction(-43, 8)), new FixedVector2(1, FixedPoint.FromFraction(37, 4)));
                CreateWall(entityManager, new FixedVector2(0, FixedPoint.FromFraction(15, 8)), new FixedVector2(1, FixedPoint.FromFraction(9, 4)));
                CreateWall(entityManager, new FixedVector2(0, 9), new FixedVector2(1, 2));
                var agent = CreateAgent(entityManager, Point(-6, 0), 4, Point(6, 0), radius);
                var clearance = LockstepNavigation.GetClearance(entityManager.GetComponentData<LockstepNavGrid>(grid), radius);

                isClear = true;
                var crossing = double.NaN;
                for (var tick = 1; tick <= 400 && !HasArrived(entityManager, agent); tick++)
                {
                    TestUtility.Step(simulation);
                    var position = Position(entityManager, agent);
                    var cells = entityManager.GetBuffer<LockstepNavCell>(grid).AsNativeArray();
                    isClear &= LockstepNavigation.IsPassable(entityManager.GetComponentData<LockstepNavGrid>(grid), cells, position, clearance);
                    if (double.IsNaN(crossing) && position.x >= FixedPoint.Zero)
                    {
                        crossing = Math.Abs((double)position.z);
                    }
                }
                Assert.IsTrue(HasArrived(entityManager, agent), $"the agent of radius {radius} arrived");
                return crossing;
            }
        }

        [Test]
        public void GroupSearch_GivesEveryAgentAPathAsShortAsItsOwn()
        {
            var grid = CreateGrid();
            // Temp memory goes with the frame: the starts are written to.
            var starts = new NativeArray<int>(9, Allocator.Temp);
            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
            using (var chain = new NativeList<int>(Allocator.Temp))
            using (var waypoints = new NativeList<FixedVector3>(Allocator.Temp))
            using (var pathfinder = new LockstepPathfinder(grid.CellCount, Allocator.Temp))
            {
                // A wall across the way, open at its top.
                Block(grid, cells, new FixedVector2(0, -2), new FixedVector2(1, 16));
                var map = new LockstepNavPathMap(grid, cells);
                var goal = Point(6, -4);
                var positions = new FixedVector3[starts.Length];
                var destinations = new FixedVector3[starts.Length];
                for (var i = 0; i < starts.Length; i++)
                {
                    var offset = Point(FixedPoint.FromFraction(6, 5) * (i % 3 - 1), FixedPoint.FromFraction(6, 5) * (i / 3 - 1));
                    positions[i] = Point(-6, -4) + offset;
                    destinations[i] = goal + offset;
                    Assert.IsTrue(LockstepPathfinder.TryGetStartCell(map, positions[i], out var cell));
                    starts[i] = grid.GetIndex(cell);
                }
                Assert.IsTrue(LockstepPathfinder.TryGetEndCell(map, goal, out var goalCell, out _));

                Assert.IsTrue(pathfinder.SearchGroup(map, grid.GetIndex(goalCell), starts), "one search reaches every agent");
                var lengths = new double[starts.Length];
                for (var i = 0; i < starts.Length; i++)
                {
                    pathfinder.GetChain(starts[i], chain);
                    Assert.AreEqual(LockstepPathStatus.Complete, pathfinder.FollowChain(map, positions[i], destinations[i], chain.AsArray(), waypoints));
                    Assert.AreEqual(destinations[i], waypoints[waypoints.Length - 1], $"agent {i} ends at its own destination");
                    AssertWalkable(grid, cells, positions[i], waypoints);
                    lengths[i] = GetLength(positions[i], waypoints);
                }
                for (var i = 0; i < starts.Length; i++)
                {
                    pathfinder.FindPath(map, positions[i], destinations[i], waypoints);
                    Assert.LessOrEqual(lengths[i], GetLength(positions[i], waypoints) * 1.02 + 0.01, $"agent {i}: the shared search costs it no detour");
                }
            }
        }

        [Test]
        public void GroupSearch_ToADestinationTheGoalDoesNotSee_LeavesTheAgentToPlanAlone()
        {
            var grid = CreateGrid();
            var starts = new NativeArray<int>(1, Allocator.Temp);
            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
            using (var chain = new NativeList<int>(Allocator.Temp))
            using (var waypoints = new NativeList<FixedVector3>(Allocator.Temp))
            using (var pathfinder = new LockstepPathfinder(grid.CellCount, Allocator.Temp))
            {
                // A wall across the grid with a gap at its right end: the goal is above it, the destination below.
                Block(grid, cells, new FixedVector2(-1, 0), new FixedVector2(18, 1));
                var map = new LockstepNavPathMap(grid, cells);
                var start = Point(-6, 6);
                var destination = Point(6, -6);
                LockstepPathfinder.TryGetStartCell(map, start, out var startCell);
                LockstepPathfinder.TryGetEndCell(map, Point(6, 6), out var goalCell, out _);
                starts[0] = grid.GetIndex(startCell);

                Assert.IsTrue(pathfinder.SearchGroup(map, grid.GetIndex(goalCell), starts));
                pathfinder.GetChain(starts[0], chain);
                Assert.AreEqual(LockstepPathStatus.Failed, pathfinder.FollowChain(map, start, destination, chain.AsArray(), waypoints),
                    "the chain of the group does not lead there");
                Assert.AreEqual(LockstepPathStatus.Complete, pathfinder.FindPath(map, start, destination, waypoints), "alone it finds the gap");
            }
        }

        private static double GetLength(FixedVector3 start, NativeList<FixedVector3> waypoints)
        {
            var length = 0.0;
            var from = start;
            for (var i = 0; i < waypoints.Length; i++)
            {
                length += (double)FixedMath.Distance(from.Xz, waypoints[i].Xz);
                from = waypoints[i];
            }
            return length;
        }

        [Test]
        public void Agents_SentTogether_ArriveEachAtItsOwnPlace()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager, FixedPoint.FromFraction(1, 4));
                CreateAvoidance(entityManager);
                // A wall across the way, open at its top; the place of the last agent lies below a ledge the goal does not see past.
                CreateWall(entityManager, new FixedVector2(0, -2), new FixedVector2(1, 16));
                CreateWall(entityManager, new FixedVector2(FixedPoint.FromFraction(13, 2), FixedPoint.FromFraction(-13, 2)), new FixedVector2(5, FixedPoint.Half));
                var goal = Point(6, -4);
                var agents = new Entity[10];
                var places = new FixedVector3[agents.Length];
                for (var i = 0; i < agents.Length; i++)
                {
                    var offset = Point(FixedPoint.FromFraction(6, 5) * (i % 3 - 1), FixedPoint.FromFraction(6, 5) * (i / 3 - 1));
                    places[i] = i < 9 ? goal + offset : Point(FixedPoint.FromFraction(13, 2), -8);
                    agents[i] = WithVelocity(entityManager, CreateAgent(entityManager, Point(-6, -4) + offset, 4, default, FixedPoint.FromFraction(2, 5)));
                    var agent = entityManager.GetComponentData<LockstepNavAgent>(agents[i]);
                    agent.SetDestination(places[i], goal);
                    entityManager.SetComponentData(agents[i], agent);
                }

                for (var tick = 1; tick <= 600 && !AllArrived(entityManager, agents); tick++)
                {
                    TestUtility.Step(simulation);
                    foreach (var agent in agents)
                    {
                        Assert.IsTrue(IsWalkable(entityManager, grid, Position(entityManager, agent)), $"tick {tick}: an agent stands on a blocked cell");
                    }
                }
                Assert.IsTrue(AllArrived(entityManager, agents), "every agent arrived");
                // An agent that arrived first may be shouldered off its place by one walking past to a place behind it, so
                // each only has to stand nearer its own place than any other.
                for (var i = 0; i < agents.Length; i++)
                {
                    var position = Position(entityManager, agents[i]).Xz;
                    var nearest = 0;
                    for (var j = 1; j < places.Length; j++)
                    {
                        if (FixedMath.DistanceSquared(position, places[j].Xz) < FixedMath.DistanceSquared(position, places[nearest].Xz))
                        {
                            nearest = j;
                        }
                    }
                    Assert.AreEqual(i, nearest, $"agent {i} stands at its own place");
                }
            }
        }

        [Test]
        public void Avoidance_PathGoesAroundACrowdOfStandingAgents()
        {
            var avoiding = PassCrowd(true, out var pushed);
            var plain = PassCrowd(false, out _);
            Assert.Greater(avoiding, 2.0, "with avoidance the path goes around the crowd");
            Assert.Less(pushed, 0.05, "and the crowd is not shouldered aside");
            Assert.Less(plain, 0.01, "without it the path goes straight through");
        }

        // A walker crosses from (-8, 0) to (8, 0) past nine agents standing in a square at the origin. Returns how far its
        // first path strays from the line along X; avoiding, walks it and returns in pushed how far the standing agents were
        // pushed in all.
        private static double PassCrowd(bool isAvoiding, out double pushed)
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager, FixedPoint.Half);
                if (isAvoiding)
                {
                    CreateAvoidance(entityManager);
                }
                var standers = new Entity[9];
                for (var i = 0; i < standers.Length; i++)
                {
                    standers[i] = WithVelocity(entityManager, CreateAgent(entityManager, Point(i % 3 - 1, i / 3 - 1), 4, default, FixedPoint.Half));
                }
                var walker = WithVelocity(entityManager, CreateAgent(entityManager, Point(-8, 0), 4, Point(8, 0), FixedPoint.Half));

                TestUtility.Step(simulation);
                var stray = 0.0;
                var path = entityManager.GetBuffer<LockstepNavWaypoint>(walker);
                for (var i = 0; i < path.Length; i++)
                {
                    stray = Math.Max(stray, Math.Abs((double)path[i].position.z));
                }

                pushed = 0;
                if (!isAvoiding)
                {
                    return stray;
                }
                WalkUntilArrived(simulation, grid, walker, 400);
                for (var i = 0; i < standers.Length; i++)
                {
                    pushed += (double)FixedMath.Distance(Position(entityManager, standers[i]), Point(i % 3 - 1, i / 3 - 1));
                }
                return stray;
            }
        }

        [Test]
        public void Walkers_MeetingInAPassageOneBodyWide_SqueezePast()
        {
            Assert.IsTrue(MeetInPassage(true), "with avoidance");
            Assert.IsTrue(MeetInPassage(false), "without avoidance");
        }

        // Two agents of radius 1/2 meet head on in a passage between walls 2 units apart, grown by the agent radius of the grid
        // to 1 unit: one body wide. Returns whether both got through.
        private static bool MeetInPassage(bool isAvoiding)
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                var grid = CreateGrid(entityManager, FixedPoint.Half);
                if (isAvoiding)
                {
                    CreateAvoidance(entityManager);
                }
                CreateWall(entityManager, new FixedVector2(0, FixedPoint.FromFraction(3, 2)), new FixedVector2(12, 1));
                CreateWall(entityManager, new FixedVector2(0, FixedPoint.FromFraction(-3, 2)), new FixedVector2(12, 1));
                var agents = new[]
                {
                    WithVelocity(entityManager, CreateAgent(entityManager, Point(-8, 0), 4, Point(8, 0), FixedPoint.Half)),
                    WithVelocity(entityManager, CreateAgent(entityManager, Point(8, 0), 4, Point(-8, 0), FixedPoint.Half)),
                };

                for (var tick = 1; tick <= 600 && !AllArrived(entityManager, agents); tick++)
                {
                    TestUtility.Step(simulation);
                    foreach (var agent in agents)
                    {
                        Assert.IsTrue(IsWalkable(entityManager, grid, Position(entityManager, agent)), $"tick {tick}: an agent stands on a blocked cell");
                    }
                }
                return AllArrived(entityManager, agents);
            }
        }

        [Test]
        public void Agent_WithAPace_WalksNoFasterThanIt()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(NavigationSystems)))
            {
                var entityManager = simulation.World.EntityManager;
                CreateGrid(entityManager);
                var slowed = CreateAgent(entityManager, Point(-8, -2), 4, Point(8, -2));
                var unhurried = CreateAgent(entityManager, Point(-8, 2), 2, Point(8, 2));
                entityManager.SetComponentData(slowed, WithPace(entityManager.GetComponentData<LockstepNavAgent>(slowed), 2));
                entityManager.SetComponentData(unhurried, WithPace(entityManager.GetComponentData<LockstepNavAgent>(unhurried), 4));

                for (var tick = 0; tick < 30; tick++)
                {
                    TestUtility.Step(simulation);
                }
                Assert.AreEqual(-6.0, (double)Position(entityManager, slowed).x, 0.01, "kept to the pace, slower than its speed");
                Assert.AreEqual(-6.0, (double)Position(entityManager, unhurried).x, 0.01, "a pace faster than its speed does not hurry it");
            }
        }

        private static LockstepNavAgent WithPace(LockstepNavAgent agent, FixedPoint pace)
        {
            agent.pace = pace;
            return agent;
        }

        [Test]
        public void Session_AgentsWalkTheSamePathsOnEveryClient()
        {
            RunSession(false);
        }

        [Test]
        public void Session_AvoidingAgentsWalkTheSamePathsOnEveryClient()
        {
            RunSession(true);
        }

        private static void RunSession(bool isAvoiding)
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
                var first = session.AddClient(SessionOptions(isAvoiding));
                var second = session.AddClient(SessionOptions(isAvoiding));
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
                            Assert.LessOrEqual((double)FixedMath.Distance(targets[i].back, transforms[i].position), isAvoiding ? 0.01 : 0.0, "every agent walked back home");
                        }
                    }
                }
            }
        }

        private static LockstepSimulationOptions SessionOptions(bool isAvoiding)
        {
            var options = TestUtility.Options(typeof(LockstepNavSystemGroup), typeof(LockstepNavObstacleSystem), typeof(LockstepNavPathSystem),
                typeof(LockstepNavAvoidanceSystem), typeof(LockstepNavMoveSystem), typeof(LockstepNavSeparationSystem), typeof(TestNavigationSystem));
            options.Initialize = world =>
            {
                var entityManager = world.EntityManager;
                var grid = CreateGrid(entityManager, FixedPoint.FromFraction(1, 4));
                if (isAvoiding)
                {
                    CreateAvoidance(entityManager);
                }
                // A round hill in the middle, two units high: the agents walk over it, and end on the plain at Y 0.
                var state = entityManager.GetComponentData<LockstepNavGrid>(grid);
                var heights = entityManager.AddBuffer<LockstepNavHeight>(grid);
                for (var z = 0; z <= state.height; z++)
                {
                    for (var x = 0; x <= state.width; x++)
                    {
                        var corner = state.origin + new FixedVector2(state.cellSize * x, state.cellSize * z);
                        heights.Add(new LockstepNavHeight { value = FixedMath.Max(FixedPoint.Zero, 2 - FixedMath.LengthSquared(corner) / 8) });
                    }
                }
                CreateWall(entityManager, new FixedVector2(-2, 3), new FixedVector2(1, 8));
                for (var i = 0; i < 6; i++)
                {
                    var z = (FixedPoint)(i * 3 - 7);
                    var agent = CreateAgent(entityManager, Point(-8, z), 3 + i, default);
                    // Their ways cross in the middle, where they push each other apart. Avoiding, they go as one group
                    // there and back, sharing one search each way.
                    entityManager.SetComponentData(agent, new LockstepNavAgent { speed = 3 + i, angularSpeed = FixedMath.Pi, radius = FixedPoint.FromFraction(2, 5) });
                    entityManager.AddComponentData(agent, new TestNavigationTarget
                    {
                        there = Point(8, -z),
                        back = Point(-8, z),
                        thereGoal = isAvoiding ? Point(8, 0) : Point(8, -z),
                        backGoal = isAvoiding ? Point(-8, 0) : Point(-8, z),
                    });
                    if (isAvoiding)
                    {
                        WithVelocity(entityManager, agent);
                    }
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

        private static Entity CreateAgent(EntityManager entityManager, FixedVector3 position, FixedPoint speed, FixedVector3 destination,
                                          FixedPoint radius = default)
        {
            var agent = entityManager.CreateEntity(typeof(LockstepTransform), typeof(LockstepNavAgent), typeof(LockstepNavWaypoint));
            entityManager.SetComponentData(agent, LockstepTransform.FromPosition(position));
            var state = new LockstepNavAgent { speed = speed, radius = radius };
            if (destination != default)
            {
                state.SetDestination(destination);
            }
            entityManager.SetComponentData(agent, state);
            return agent;
        }

        // Local avoidance with the default settings, as a LockstepNavAvoidanceAuthoring bakes it.
        private static void CreateAvoidance(EntityManager entityManager)
        {
            var avoidance = entityManager.CreateEntity(typeof(LockstepNavAvoidance));
            entityManager.SetComponentData(avoidance, LockstepNavAvoidance.Default);
        }

        // The agent takes part in avoidance, as LockstepNavAgentAuthoring bakes it.
        private static Entity WithVelocity(EntityManager entityManager, Entity agent)
        {
            entityManager.AddComponent<LockstepNavVelocity>(agent);
            return agent;
        }

        private static bool HasArrived(EntityManager entityManager, Entity agent)
        {
            return entityManager.GetComponentData<LockstepNavAgent>(agent).status == LockstepNavStatus.Arrived;
        }

        private static bool AllArrived(EntityManager entityManager, Entity[] agents)
        {
            foreach (var agent in agents)
            {
                if (!HasArrived(entityManager, agent))
                {
                    return false;
                }
            }
            return true;
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

        private static FixedVector3 Position(EntityManager entityManager, Entity agent)
        {
            return entityManager.GetComponentData<LockstepTransform>(agent).position;
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
