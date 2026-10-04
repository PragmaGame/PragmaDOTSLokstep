using System;
using Pragma.Lockstep.Views;
using Unity.Entities;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pragma.Lockstep.Tests
{
    /// <summary>
    /// A presentation world with the GameObject view systems, two view prefabs bound to the keys of
    /// <see cref="TestViewSpawnSystem"/> and a runtime catalog for them.
    /// </summary>
    internal sealed class ViewHarness : IDisposable
    {
        private readonly TransformViewUpdateSystem _transformSystem;
        private readonly SystemBase _dataSystem;
        private readonly TestTimeViewUpdateSystem _timeSystem;
        private readonly TestViewHiddenUpdateSystem _hiddenSystem;
        private readonly TestViewElementUpdateSystem _elementSystem;
        private readonly bool _isConfigRegistered;

        /// <param name="registerConfig">Registers the catalog of the two prefabs at runtime.</param>
        /// <param name="visibleOnly">Pushes data with <see cref="TestVisibleViewDataUpdateSystem"/> instead of <see cref="TestViewDataUpdateSystem"/>.</param>
        public ViewHarness(bool registerConfig = true, bool visibleOnly = false)
        {
            World = new World("Presentation");
            ManagerSystem = World.GetOrCreateSystemManaged<EntityViewManagerSystem>();
            _transformSystem = World.GetOrCreateSystemManaged<TransformViewUpdateSystem>();
            _dataSystem = visibleOnly
                ? World.GetOrCreateSystemManaged<TestVisibleViewDataUpdateSystem>()
                : World.GetOrCreateSystemManaged<TestViewDataUpdateSystem>();
            _timeSystem = World.GetOrCreateSystemManaged<TestTimeViewUpdateSystem>();
            _hiddenSystem = World.GetOrCreateSystemManaged<TestViewHiddenUpdateSystem>();
            _elementSystem = World.GetOrCreateSystemManaged<TestViewElementUpdateSystem>();

            PrefabA = CreatePrefab("TestA view");
            PrefabB = CreatePrefab("TestB view");
            Config = ScriptableObject.CreateInstance<EntityViewConfig>();
            Config.SetBinders(new EntityViewBinder(TestViewSpawnSystem.KEY_A, PrefabA), new EntityViewBinder(TestViewSpawnSystem.KEY_B, PrefabB));
            if (registerConfig)
            {
                EntityViewConfigs.Add(Config);
                _isConfigRegistered = true;
            }
        }

        public World World { get; }
        public EntityViewManagerSystem ManagerSystem { get; }
        public EntityViewManager Manager => ManagerSystem.Manager;
        public EntityView PrefabA { get; }
        public EntityView PrefabB { get; }
        public EntityViewConfig Config { get; }

        /// <summary>
        /// A view prefab (an inactive template object) with a transform part, both test data parts, a time part, a tag part and
        /// a buffer part.
        /// </summary>
        public static EntityView CreatePrefab(string name)
        {
            var gameObject = new GameObject(name);
            gameObject.SetActive(false);
            var view = gameObject.AddComponent<EntityView>();
            gameObject.AddComponent<TransformComponentView>();
            gameObject.AddComponent<TestViewDataView>();
            gameObject.AddComponent<TestViewDataRawView>();
            gameObject.AddComponent<TestTimeView>();
            gameObject.AddComponent<TestViewHiddenView>();
            gameObject.AddComponent<TestViewElementView>();
            return view;
        }

        public void Show(LockstepClient client)
        {
            LockstepWorlds.RegisterClient(World, client);
        }

        /// <summary>The presentation update of one frame, in the order of the system groups.</summary>
        public void Update()
        {
            ManagerSystem.Update();
            _transformSystem.Update();
            _dataSystem.Update();
            _timeSystem.Update();
            _hiddenSystem.Update();
            _elementSystem.Update();
        }

        /// <summary>A frame in which only the manager system ran, as if the update systems were disabled.</summary>
        public void UpdateManagerOnly()
        {
            ManagerSystem.Update();
        }

        /// <summary>Steps the session frame by frame until <paramref name="client"/> has simulated <paramref name="lastTick"/>.</summary>
        public void Run(SessionHarness session, LockstepClient client, int lastTick, Action onFrame = null)
        {
            for (var frame = 0; client.SimulatedTicks - 1 < lastTick; frame++)
            {
                if (frame > 10000)
                {
                    throw new TimeoutException($"Tick {lastTick} was never simulated.");
                }
                session.Step(1 / 60.0);
                Update();
                onFrame?.Invoke();
            }
        }

        /// <summary>The single view spawned now, or null.</summary>
        public EntityView SpawnedView()
        {
            foreach (var pair in Manager.Views)
            {
                return pair.Value;
            }
            return null;
        }

        public void Dispose()
        {
            LockstepWorlds.UnregisterClient(World);
            if (_isConfigRegistered)
            {
                EntityViewConfigs.Remove(Config);
            }
            if (World.IsCreated)
            {
                World.Dispose();
            }
            Object.DestroyImmediate(PrefabA.gameObject);
            Object.DestroyImmediate(PrefabB.gameObject);
            Object.DestroyImmediate(Config);
        }
    }
}
