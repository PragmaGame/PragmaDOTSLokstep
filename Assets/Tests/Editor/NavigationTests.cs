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
                var grid = CreateGrid(entityManager);
                CreateWall(entityManager, FixedVector2.Zero, new FixedVector2(1, 12));
                // The right agent stands at the wall: its push goes into it, so only the left one gives way.
                var left = CreateAgent(entityManager, Point(FixedPoint.FromFraction(-3, 4), 0), 4, default, FixedPoint.Half);
                var right = CreateAgent(entityManager, Point(FixedPoint.FromFraction(-11, 20), 0), 4, default, FixedPoint.Half);

                for (var tick = 1; tick <= 40; tick++)
                {
                    TestUtility.Step(simulation);
                    Assert.IsTrue(IsWalkable(entityManager, grid, Position(entityManager, left)), $"tick {tick}: left agent on a blocked cell");
                    Assert.IsTrue(IsWalkable(entityManager, grid, Position(entityManager, right)), $"tick {tick}: right agent on a blocked cell");
                }
                // Pushes small enough to keep it on its cell bring it up to the wall, never away from it.
                Assert.GreaterOrEqual((double)Position(entityManager, right).x, -0.55, "the agent at the wall does not give way");
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
                typeof(LockstepNavMoveSystem), typeof(LockstepNavSeparationSystem), typeof(TestNavigationSystem));
            options.Initialize = world =>
            {
                var entityManager = world.EntityManager;
                var grid = CreateGrid(entityManager, FixedPoint.FromFraction(1, 4));
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
                    // Their ways cross in the middle, where they push each other apart.
                    entityManager.SetComponentData(agent, new LockstepNavAgent { speed = 3 + i, angularSpeed = FixedMath.Pi, radius = FixedPoint.FromFraction(2, 5) });
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
