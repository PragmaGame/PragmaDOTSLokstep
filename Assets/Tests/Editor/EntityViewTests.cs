using System.Text.RegularExpressions;
using NUnit.Framework;
using Pragma.Lockstep.Views;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Pragma.Lockstep.Tests
{
    /// <summary>GameObject views (the adapted ECV) driven by <see cref="TestViewSpawnSystem"/>.</summary>
    public class EntityViewTests
    {
        private static LockstepServerSettings Settings(int minPlayers = 1)
        {
            var settings = LockstepServerSettings.Default;
            settings.TickRate = 30;
            settings.InputSize = UnsafeUtility.SizeOf<TestInput>();
            settings.MinPlayersToStart = minPlayers;
            settings.StartDelaySeconds = 0;
            settings.ChecksumInterval = 5;
            return settings;
        }

        private static LockstepClient AddClient(SessionHarness session)
        {
            return session.AddClient(TestUtility.Options(typeof(TestViewSpawnSystem)));
        }

        private static Entity SimulatedEntity(LockstepClient client)
        {
            using (var query = client.Simulation.World.EntityManager.CreateEntityQuery(typeof(TestViewData)))
            {
                return query.CalculateEntityCount() == 1 ? query.GetSingletonEntity() : Entity.Null;
            }
        }

        [Test]
        public void Views_FollowTheirEntityAndGoBackToThePool()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness())
            {
                var client = AddClient(session);
                views.Show(client);
                EntityView first = null;
                var lastX = float.MinValue;
                views.Run(session, client, 24, () =>
                {
                    var tick = client.SimulatedTicks - 1;
                    if (tick < 3)
                    {
                        Assert.AreEqual(0, views.Manager.Views.Count);
                        return;
                    }
                    var view = views.SpawnedView();
                    Assert.IsNotNull(view, $"tick {tick}");
                    if (first == null)
                    {
                        first = view;
                    }
                    Assert.AreSame(first, view, "one view for the whole life of the key");
                    Assert.AreEqual(SimulatedEntity(client), view.Entity);
                    Assert.AreSame(client, view.Client);
                    Assert.IsTrue(view.gameObject.activeSelf);
                    Assert.AreEqual("TestA view", view.name);
                    var x = view.transform.position.x;
                    Assert.GreaterOrEqual(x, lastX - 1e-4f, "the view moves forward smoothly");
                    Assert.That(x, Is.InRange(10f, 11f + tick - 3 + 1e-3f));
                    lastX = x;
                });

                // Tick 25 swaps the key: the A view goes back to the pool and a B view takes over the entity.
                views.Run(session, client, 25);
                var swapped = views.SpawnedView();
                Assert.AreEqual("TestB view", swapped.name);
                Assert.AreEqual(SimulatedEntity(client), swapped.Entity);
                Assert.IsFalse(first.IsBound);
                Assert.IsFalse(first.gameObject.activeSelf, "released views wait inactive in the pool");

                views.Run(session, client, 30);
                Assert.AreEqual(0, views.Manager.Views.Count, "the view goes away with the entity");
                Assert.IsFalse(swapped.gameObject.activeSelf);

                // Tick 33 creates another key A entity, which gets the pooled instance.
                views.Run(session, client, 33);
                Assert.AreSame(first, views.SpawnedView());
                Assert.IsTrue(first.gameObject.activeSelf);
            }
        }

        [Test]
        public void ViewData_IsPushedWhenItChanges()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness())
            {
                var client = AddClient(session);
                views.Show(client);
                views.Run(session, client, 24);
                var first = views.SpawnedView();
                var raw = first.GetComponent<TestViewDataRawView>();
                var cached = first.GetComponent<TestViewDataView>();
                CollectionAssert.AreEqual(new[] { 0, 1, 2 }, raw.values, "one push per write, none on the other ticks");
                CollectionAssert.AreEqual(new[] { 0, 1, 2 }, cached.values);

                views.Run(session, client, 25);
                CollectionAssert.AreEqual(new[] { 2 }, views.SpawnedView().GetComponent<TestViewDataRawView>().values,
                    "a new view starts from the current values");
                Assert.AreEqual(1, cached.bindBreakCount);

                views.Run(session, client, 33);
                Assert.AreSame(first, views.SpawnedView());
                Assert.AreEqual(2, cached.bindCount);
                CollectionAssert.AreEqual(new[] { 0, 1, 2, 2 }, cached.values, "binding again forgets the previous owner's value");
            }
        }

        [Test]
        public void BufferViews_GetTheBufferWhenItIsWritten()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness())
            {
                var client = AddClient(session);
                views.Show(client);
                views.Run(session, client, 24);
                var first = views.SpawnedView();
                var part = first.GetComponent<TestViewElementView>();
                CollectionAssert.AreEqual(new[] { "1", "1,2" }, part.contents, "the buffer of the new view, then one push per write");

                views.Run(session, client, 25);
                CollectionAssert.AreEqual(new[] { "1,2" }, views.SpawnedView().GetComponent<TestViewElementView>().contents,
                    "a new view starts from the current buffer");

                views.Run(session, client, 33);
                Assert.AreSame(first, views.SpawnedView());
                CollectionAssert.AreEqual(new[] { "1", "1,2", "3" }, part.contents);
            }
        }

        [Test]
        public void AutoUpdate_PausesAndCatchesUp()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness())
            {
                var client = AddClient(session);
                views.Show(client);
                views.Run(session, client, 5);
                var view = views.SpawnedView();
                var raw = view.GetComponent<TestViewDataRawView>();
                view.IsAutoUpdateEnabled = false;

                views.Run(session, client, 12);
                CollectionAssert.AreEqual(new[] { 0 }, raw.values, "nothing reaches a paused view");

                view.IsAutoUpdateEnabled = true;
                views.Run(session, client, 13);
                CollectionAssert.AreEqual(new[] { 0, 1 }, raw.values, "turning it on pushes the current value");
            }
        }

        [Test]
        public void AttachedViews_GetDataUntilTheirEntityGoes()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness())
            {
                var client = AddClient(session);
                views.Show(client);
                views.Run(session, client, 5);
                var hud = ViewHarness.CreatePrefab("HUD");
                hud.gameObject.SetActive(true);
                try
                {
                    var entity = SimulatedEntity(client);
                    Assert.IsTrue(views.Manager.Attach(entity, hud));
                    Assert.IsFalse(views.Manager.Attach(entity, hud), "a bound view is not attached twice");
                    Assert.IsTrue(hud.IsAttached);

                    views.Run(session, client, 12);
                    var raw = hud.GetComponent<TestViewDataRawView>();
                    CollectionAssert.AreEqual(new[] { 0, 1 }, raw.values, "the current value when attached, then the changes");
                    Assert.Greater(hud.transform.position.x, 10f, "attached views move too");

                    views.Run(session, client, 26);
                    Assert.AreEqual(entity, hud.Entity, "a key change does not touch attached views");

                    views.Run(session, client, 30);
                    Assert.IsFalse(hud.IsBound, "the view is detached when its entity goes away");
                    Assert.AreEqual(1, hud.GetComponent<TestViewDataView>().bindBreakCount);
                    Assert.IsTrue(hud.gameObject.activeSelf, "an attached view is never pooled");
                }
                finally
                {
                    Object.DestroyImmediate(hud.gameObject);
                }
            }
        }

        [Test]
        public void Views_LeaveTheSimulationUntouched()
        {
            using (var session = new SessionHarness(Settings(minPlayers: 2), latency: 0.03, jitter: 0.02))
            using (var views = new ViewHarness())
            {
                var shown = AddClient(session);
                var hidden = AddClient(session);
                views.Show(shown);
                var sawView = false;
                views.Run(session, shown, 45, () => sawView |= views.Manager.Views.Count > 0);

                Assert.IsTrue(sawView);
                Assert.IsEmpty(session.Desyncs);
                TestUtility.AssertSameChecksums(shown, hidden, 5);
            }
        }

        [Test]
        public void ViewCatalog_ReadsBakedRegistries()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness(registerConfig: false))
            {
                var entityManager = views.World.EntityManager;
                var registry = entityManager.CreateEntity(typeof(EntityViewRegistry), typeof(EntityViewPrefabElement));
                entityManager.GetBuffer<EntityViewPrefabElement>(registry).Add(new EntityViewPrefabElement
                {
                    key = TestViewSpawnSystem.KEY_A,
                    prefab = views.PrefabA.gameObject,
                });
                var client = AddClient(session);
                views.Show(client);

                views.Run(session, client, 5);
                Assert.AreEqual("TestA view", views.SpawnedView().name);
                views.Run(session, client, 26);
                Assert.AreEqual(0, views.Manager.Views.Count, "key B has no binder in this catalog");
            }
        }

        [Test]
        public void PoolFactory_SuppliesThePoolOfEachWorld()
        {
            TestViewPool pool = null;
            EntityViewManagerSystem.PoolFactory = world => pool = new TestViewPool();
            try
            {
                using (var session = new SessionHarness(Settings()))
                {
                    var views = new ViewHarness();
                    try
                    {
                        var client = AddClient(session);
                        views.Show(client);
                        views.Run(session, client, 26);
                        Assert.IsNotNull(pool);
                        Assert.AreEqual(2, pool.SpawnCount, "key A, then key B");
                        Assert.AreEqual(1, pool.ReleaseCount);
                    }
                    finally
                    {
                        views.Dispose();
                    }
                    Assert.AreEqual(2, pool.ReleaseCount, "destroying the world returns its views");
                    Assert.IsTrue(pool.IsDisposed, "the manager owns what the factory made");
                }
            }
            finally
            {
                EntityViewManagerSystem.PoolFactory = null;
            }
        }

        [Test]
        public void Pool_SetByHandBelongsToTheCaller()
        {
            var pool = new TestViewPool();
            using (var session = new SessionHarness(Settings()))
            {
                var views = new ViewHarness();
                try
                {
                    var client = AddClient(session);
                    views.Show(client);
                    views.Run(session, client, 5);
                    var pooled = views.SpawnedView();

                    views.ManagerSystem.Pool = pool;
                    Assert.IsFalse(pooled.IsBound, "the views go back to the previous pool");
                    views.Run(session, client, 6);
                    Assert.AreEqual(1, pool.SpawnCount, "and come back from the new one");
                }
                finally
                {
                    views.Dispose();
                }
                Assert.AreEqual(1, pool.ReleaseCount);
                Assert.IsFalse(pool.IsDisposed);
            }
        }

        [Test]
        public void Views_GoBackWhenTheSessionEnds()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness())
            {
                var client = AddClient(session);
                views.Show(client);
                views.Run(session, client, 5);
                var view = views.SpawnedView();

                LockstepWorlds.UnregisterClient(views.World);
                views.Update();
                Assert.AreEqual(0, views.Manager.Views.Count);
                Assert.IsNull(views.Manager.Client);
                Assert.IsFalse(view.IsBound);
                Assert.IsFalse(view.gameObject.activeSelf);
            }
        }

        [Test]
        public void HiddenWorlds_ReturnTheirViewsAndShowTheCurrentState()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness())
            {
                var client = AddClient(session);
                views.Show(client);
                views.Run(session, client, 5);
                var view = views.SpawnedView();

                views.ManagerSystem.IsShown = false;
                Assert.AreEqual(0, views.Manager.Views.Count, "hiding returns the views at once");
                Assert.IsFalse(view.IsBound);
                Assert.IsFalse(view.gameObject.activeSelf);

                views.Run(session, client, 24);
                Assert.AreEqual(0, views.Manager.Views.Count, "a hidden world spawns nothing");

                views.ManagerSystem.IsShown = true;
                views.Update();
                var shown = views.SpawnedView();
                Assert.AreSame(view, shown, "the pooled instance comes back");
                Assert.AreEqual(SimulatedEntity(client), shown.Entity);
                CollectionAssert.AreEqual(new[] { 0, 2 }, shown.GetComponent<TestViewDataRawView>().values,
                    "nothing is pushed while hidden, and the view shown again starts from the current value");
            }
        }

        [Test]
        public void PausedViews_ComeBackFromThePoolAwake()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness())
            {
                var client = AddClient(session);
                views.Show(client);
                views.Run(session, client, 5);
                var first = views.SpawnedView();
                first.IsAutoUpdateEnabled = false;

                // Tick 25 returns the paused view to the pool, tick 33 hands it to a new entity.
                views.Run(session, client, 33);
                Assert.AreSame(first, views.SpawnedView());
                Assert.IsTrue(first.IsAutoUpdateEnabled, "a previous owner's pause does not follow the instance");
                CollectionAssert.AreEqual(new[] { 0, 2 }, first.GetComponent<TestViewDataRawView>().values);
            }
        }

        [Test]
        public void ViewData_FollowsEnableableComponentsOfTheQuery()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness(visibleOnly: true))
            {
                var client = AddClient(session);
                views.Show(client);
                views.Run(session, client, 11);
                var raw = views.SpawnedView().GetComponent<TestViewDataRawView>();
                CollectionAssert.AreEqual(new[] { 0 }, raw.values, "the write on tick 10 happened while the entity was hidden");

                views.Run(session, client, 12);
                CollectionAssert.AreEqual(new[] { 0, 1 }, raw.values, "turning TestViewHidden off brings the entity back with its value");
            }
        }

        [Test]
        public void ViewData_TagsArePushedWhenTheyTurnOn()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness())
            {
                var client = AddClient(session);
                views.Show(client);
                views.Run(session, client, 7);
                var part = views.SpawnedView().GetComponent<TestViewHiddenView>();
                Assert.AreEqual(0, part.pushCount, "a disabled tag is absent");

                views.Run(session, client, 8);
                Assert.AreEqual(1, part.pushCount, "turning it on pushes it");

                views.Run(session, client, 20);
                Assert.AreEqual(1, part.pushCount, "turning it off pushes nothing: parts learn values, not absence");
            }
        }

        [Test]
        public void ViewData_SeesWritesMadeOutsideSystems()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness())
            {
                var client = AddClient(session);
                views.Show(client);
                views.Run(session, client, 2);
                var hud = ViewHarness.CreatePrefab("Clock");
                hud.gameObject.SetActive(true);
                try
                {
                    // The session entity's LockstepTime is written by LockstepSimulation.Step, outside every system.
                    using (var query = client.Simulation.World.EntityManager.CreateEntityQuery(typeof(LockstepTime)))
                    {
                        Assert.IsTrue(views.Manager.Attach(query.GetSingletonEntity(), hud));
                    }
                    var ticks = hud.GetComponent<TestTimeView>().ticks;
                    views.Run(session, client, 12, () =>
                    {
                        Assert.IsNotEmpty(ticks);
                        Assert.AreEqual(client.SimulatedTicks - 1, ticks[ticks.Count - 1], "every tick reaches the view");
                    });
                }
                finally
                {
                    Object.DestroyImmediate(hud.gameObject);
                }
            }
        }

        [Test]
        public void Views_DestroyedFromOutsideAreReplacedWithoutATick()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness())
            {
                var client = AddClient(session);
                views.Show(client);
                views.Run(session, client, 5);
                var destroyed = views.SpawnedView();
                Object.DestroyImmediate(destroyed.gameObject);

                // No session step: the simulation does not change, the presentation notices the loss on its own.
                views.Update();
                views.Update();
                var replacement = views.SpawnedView();
                Assert.IsTrue(replacement != null, "a new view replaces the destroyed one");
                Assert.AreEqual(SimulatedEntity(client), replacement.Entity);
                CollectionAssert.AreEqual(new[] { 0 }, replacement.GetComponent<TestViewDataRawView>().values);
            }
        }

        [Test]
        public void AttachedViews_DetachAndForceUpdate()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness())
            {
                var client = AddClient(session);
                views.Show(client);
                views.Run(session, client, 12);
                var hud = ViewHarness.CreatePrefab("HUD");
                hud.gameObject.SetActive(true);
                try
                {
                    var entity = SimulatedEntity(client);
                    views.Manager.Attach(entity, hud);
                    views.Update();
                    var raw = hud.GetComponent<TestViewDataRawView>();
                    CollectionAssert.AreEqual(new[] { 1 }, raw.values);

                    views.Manager.ForceUpdate(entity);
                    views.Update();
                    CollectionAssert.AreEqual(new[] { 1, 1 }, raw.values, "a forced update repeats the current value");

                    views.Manager.Detach(hud);
                    Assert.IsFalse(hud.IsBound);
                    Assert.IsFalse(hud.IsAttached);
                    Assert.AreEqual(1, hud.GetComponent<TestViewDataView>().bindBreakCount);
                    views.Run(session, client, 21);
                    CollectionAssert.AreEqual(new[] { 1, 1 }, raw.values, "a detached view gets nothing more");
                    Assert.IsTrue(views.Manager.Attach(entity, hud), "and can be attached again");
                }
                finally
                {
                    Object.DestroyImmediate(hud.gameObject);
                }
            }
        }

        [Test]
        public void Views_FollowANewSession()
        {
            using (var session = new SessionHarness(Settings(minPlayers: 2)))
            using (var views = new ViewHarness())
            {
                var a = AddClient(session);
                var b = AddClient(session);
                views.Show(a);
                views.Run(session, a, 5);
                var view = views.SpawnedView();
                Assert.AreSame(a, view.Client);

                views.Show(b);
                views.Update();
                var shown = views.SpawnedView();
                Assert.AreSame(b, shown.Client, "the views now show the other session");
                Assert.AreEqual(SimulatedEntity(b), shown.Entity);
                Assert.AreEqual(1, views.Manager.Views.Count);
                if (view != shown)
                {
                    Assert.IsFalse(view.IsBound, "the view of the old session went back to the pool");
                }
                var values = shown.GetComponent<TestViewDataRawView>().values;
                Assert.AreEqual(0, values[values.Count - 1], "the new session's values are pushed at once");
            }
        }

        [Test]
        public void ViewCatalog_ChangesApplyDuringASession()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness())
            {
                var client = AddClient(session);
                views.Show(client);
                views.Run(session, client, 5);
                Assert.AreEqual("TestA view", views.SpawnedView().name);

                views.Config.SetBinders(new EntityViewBinder(TestViewSpawnSystem.KEY_A, views.PrefabB));
                views.Update();
                Assert.AreEqual("TestB view", views.SpawnedView().name, "the new binder takes over without a tick");
            }
        }

        [Test]
        public void ViewCatalog_BakedBindersComeFirst()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness())
            {
                var entityManager = views.World.EntityManager;
                var registry = entityManager.CreateEntity(typeof(EntityViewRegistry), typeof(EntityViewPrefabElement));
                entityManager.GetBuffer<EntityViewPrefabElement>(registry).Add(new EntityViewPrefabElement
                {
                    key = TestViewSpawnSystem.KEY_A,
                    prefab = views.PrefabB.gameObject,
                });
                var client = AddClient(session);
                views.Show(client);

                LogAssert.Expect(LogType.Warning, new Regex("bound twice"));
                views.Run(session, client, 5);
                Assert.AreEqual("TestB view", views.SpawnedView().name);
            }
        }

        [Test]
        public void ViewCatalog_SeesEditsMadeWithoutASession()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness(registerConfig: false))
            {
                var entityManager = views.World.EntityManager;
                var registry = entityManager.CreateEntity(typeof(EntityViewRegistry), typeof(EntityViewPrefabElement));
                entityManager.GetBuffer<EntityViewPrefabElement>(registry).Add(new EntityViewPrefabElement
                {
                    key = TestViewSpawnSystem.KEY_A,
                    prefab = views.PrefabA.gameObject,
                });
                var client = AddClient(session);
                views.Show(client);
                views.Run(session, client, 5);
                LockstepWorlds.UnregisterClient(views.World);
                views.Update();

                // Live baking edits a registry in place: only its change version tells.
                var elements = entityManager.GetBuffer<EntityViewPrefabElement>(registry);
                elements[0] = new EntityViewPrefabElement
                {
                    key = TestViewSpawnSystem.KEY_A,
                    prefab = views.PrefabB.gameObject,
                };
                views.Update();
                views.Update();

                views.Show(client);
                views.Update();
                Assert.AreEqual("TestB view", views.SpawnedView().name);
            }
        }

        [Test]
        public void ViewData_IsPushedInFullAfterAMissedFrame()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness())
            {
                var client = AddClient(session);
                views.Show(client);
                views.Run(session, client, 5);
                var hud = ViewHarness.CreatePrefab("HUD");
                hud.gameObject.SetActive(true);
                try
                {
                    views.Manager.Attach(SimulatedEntity(client), hud);
                    // The frame that would have pushed the attached view's first values runs without the update systems.
                    views.UpdateManagerOnly();
                    views.UpdateManagerOnly();
                    views.Update();
                    CollectionAssert.AreEqual(new[] { 0 }, hud.GetComponent<TestViewDataRawView>().values);
                }
                finally
                {
                    Object.DestroyImmediate(hud.gameObject);
                }
            }
        }

        [Test]
        public void PoolFactory_ReturningNullFallsBackToTheDefaultPool()
        {
            EntityViewManagerSystem.PoolFactory = world => null;
            try
            {
                using (var session = new SessionHarness(Settings()))
                using (var views = new ViewHarness())
                {
                    var client = AddClient(session);
                    views.Show(client);
                    views.Run(session, client, 5);
                    Assert.IsInstanceOf<EntityViewPool>(views.ManagerSystem.Pool);
                    Assert.IsNotNull(views.SpawnedView());
                }
            }
            finally
            {
                EntityViewManagerSystem.PoolFactory = null;
            }
        }

        [Test]
        public void EntityViewPool_ReusesInstancesPerPrefab()
        {
            var prefab = ViewHarness.CreatePrefab("Pooled");
            var pool = new EntityViewPool("Test Pool");
            try
            {
                pool.Prewarm(prefab, 2);
                Assert.AreEqual(2, pool.InactiveCount);

                var first = pool.Spawn(prefab);
                var second = pool.Spawn(prefab);
                Assert.AreEqual(0, pool.InactiveCount);
                Assert.IsTrue(first.gameObject.activeSelf);
                Assert.AreEqual("Pooled", first.name);
                Assert.AreNotSame(first, second);

                pool.Release(first);
                Assert.IsFalse(first.gameObject.activeSelf);
                Assert.AreSame(pool.Root, first.transform.parent);
                Assert.AreSame(first, pool.Spawn(prefab), "released instances are reused");

                var root = pool.Root.gameObject;
                pool.Dispose();
                Assert.IsTrue(first == null && second == null && root == null, "disposing destroys every instance and the root");
            }
            finally
            {
                pool.Dispose();
                Object.DestroyImmediate(prefab.gameObject);
            }
        }

        [Test]
        public void EntityView_ReadsTheDataOfItsEntity()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness())
            {
                var client = AddClient(session);
                views.Show(client);
                views.Run(session, client, 10);
                var view = views.SpawnedView();
                Assert.IsTrue(view.TryGetData<TestViewData>(out var data));
                Assert.AreEqual(1, data.value);
                Assert.IsTrue(view.TryGetData<TestViewHidden>(out _), "a tag is read on or off");
                Assert.IsFalse(view.TryGetData<LockstepEntityId>(out _), "the entity has no id");
                Assert.IsTrue(view.TryGetBuffer<TestViewElement>(out var elements));
                Assert.AreEqual(1, elements.Length);
                Assert.AreEqual(1, elements[0].value);

                // Tick 25 changes the key, which returns this view to the pool.
                views.Run(session, client, 25);
                Assert.IsFalse(view.TryGetData<TestViewData>(out _), "an unbound view reads nothing");
                Assert.IsFalse(view.TryGetBuffer<TestViewElement>(out _));
            }
        }

        [Test]
        public void EntityView_LeavesNestedViewsTheirParts()
        {
            using (var session = new SessionHarness(Settings()))
            using (var views = new ViewHarness())
            {
                var client = AddClient(session);
                views.Show(client);
                views.Run(session, client, 5);
                var hud = ViewHarness.CreatePrefab("HUD");
                var nested = ViewHarness.CreatePrefab("Nested");
                nested.transform.SetParent(hud.transform, false);
                nested.gameObject.SetActive(true);
                hud.gameObject.SetActive(true);
                try
                {
                    Assert.IsTrue(views.Manager.Attach(SimulatedEntity(client), hud));
                    views.Update();
                    CollectionAssert.AreEqual(new[] { 0 }, hud.GetComponent<TestViewDataRawView>().values);
                    CollectionAssert.IsEmpty(nested.GetComponent<TestViewDataRawView>().values, "the parts under a nested view are its own");
                    Assert.IsFalse(nested.IsBound);
                }
                finally
                {
                    Object.DestroyImmediate(hud.gameObject);
                }
            }
        }

        [Test]
        public void EntityViewKey_FitsTwentyNineBytes()
        {
            Assert.IsFalse(EntityViewKey.TryCreate(null, out _));
            Assert.IsFalse(EntityViewKey.TryCreate("", out _));
            Assert.IsFalse(EntityViewKey.TryCreate(new string('x', 30), out _));
            Assert.IsTrue(EntityViewKey.TryCreate(new string('x', 29), out var key));
            Assert.AreEqual(29, key.value.Length);
        }
    }
}
