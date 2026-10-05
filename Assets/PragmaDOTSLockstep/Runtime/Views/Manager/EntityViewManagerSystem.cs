using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// The only place GameObject views are spawned and returned. For every simulation entity with an
    /// <see cref="EntityViewKey"/> the catalogs know, it takes a view from the pool and binds it; it returns the view when
    /// the entity goes away or its key changes.
    /// </summary>
    /// <remarks>
    /// Runs in presentation worlds and shows the session registered for the world in <see cref="LockstepWorlds"/>. The
    /// simulation world is only read: nothing, not even a cleanup component, may be added to simulated entities, so
    /// lifetimes are found by comparing the keyed entities with the views whenever a tick created, destroyed or re-keyed
    /// some. Catalogs come from <see cref="EntityViewRegistry"/> entities baked into this world, then from
    /// <see cref="EntityViewConfigs"/>.
    /// </remarks>
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
    public partial class EntityViewManagerSystem : SystemBase
    {
        private readonly EntityViewManager _manager = new EntityViewManager();
        private readonly Dictionary<FixedString32Bytes, EntityView> _prefabs = new Dictionary<FixedString32Bytes, EntityView>();
        private readonly List<(Entity Entity, EntityView Prefab, FixedString32Bytes Key)> _toSpawn = new List<(Entity, EntityView, FixedString32Bytes)>();
        private readonly List<Entity> _toRelease = new List<Entity>();
        private IEntityViewPool _pool;
        private bool _isPoolOwned;
        private EntityQuery _registryQuery;
        private int _registryOrderVersion = -1;
        private int _configsVersion = -1;
        private World _simulationWorld;
        private EntityQuery _keyQuery;
        private uint _scannedVersion;
        private int _keyOrderVersion;
        private bool _isScanRequired;
        private int _scanStamp;
        private bool _isShown = true;

        /// <summary>
        /// Creates the pool of every presentation world. Set it once per project from the game's bootstrap, before
        /// presentation worlds spawn views; null uses <see cref="EntityViewPool"/>. It is cleared when play mode starts.
        /// </summary>
        /// <remarks>
        /// The manager owns what the factory returns: when the world is destroyed it returns its views to that pool and
        /// disposes the pool if it is <see cref="IDisposable"/>. To share one pool between worlds, return a thin wrapper
        /// per world.
        /// </remarks>
        public static Func<World, IEntityViewPool> PoolFactory { get; set; }

        /// <summary>The views of this world, by simulation entity.</summary>
        public EntityViewManager Manager => _manager;

        /// <summary>
        /// Whether this world shows its session. Hiding returns every view to the pool at once, and a hidden world spawns
        /// none; shown again, it spawns the views of the current state. One process can run several presentation worlds
        /// (the local players of a hot-seat test, a spectator) and show one of them.
        /// </summary>
        public bool IsShown
        {
            get => _isShown;
            set
            {
                if (_isShown == value)
                {
                    return;
                }
                _isShown = value;
                if (!value)
                {
                    EndSession();
                }
            }
        }

        /// <summary>
        /// The pool of this world: the one from <see cref="PoolFactory"/>, or one set here. Setting it returns the spawned
        /// views to the previous pool first; the caller keeps ownership of a pool it sets.
        /// </summary>
        public IEntityViewPool Pool
        {
            get
            {
                if (_pool == null)
                {
                    _pool = PoolFactory?.Invoke(World) ?? new EntityViewPool($"Entity Views ({World.Name})");
                    _isPoolOwned = true;
                }
                return _pool;
            }
            set
            {
                if (ReferenceEquals(value, _pool))
                {
                    return;
                }
                ReleaseAll();
                DisposeOwnedPool();
                _pool = value;
                _isPoolOwned = false;
                _isScanRequired = true;
            }
        }

        protected override void OnCreate()
        {
            _registryQuery = GetEntityQuery(ComponentType.ReadOnly<EntityViewRegistry>(), ComponentType.ReadOnly<EntityViewPrefabElement>());
        }

        protected override void OnDestroy()
        {
            EndSession();
            DisposeOwnedPool();
        }

        protected override void OnUpdate()
        {
            _manager.BeginFrame();
            // Watched without a session too: a registry edited in place is seen only by the update right after the edit.
            if (HasCatalogChanged())
            {
                RebuildCatalog();
                _isScanRequired = true;
            }
            if (!_isShown)
            {
                return;
            }

            LockstepWorlds.TryGetClient(World, out var client);
            var simulation = client?.Simulation;
            if (simulation == null || !simulation.World.IsCreated)
            {
                EndSession();
                return;
            }
            if (client != _manager.Client || simulation.World != _simulationWorld)
            {
                EndSession();
                _simulationWorld = simulation.World;
                _keyQuery = _simulationWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<EntityViewKey>());
                _manager.SetClient(client);
                _isScanRequired = true;
            }

            // Between ticks the simulation does not change, so neither do the views it needs. Most ticks create, destroy
            // and re-key nothing, so the keyed entities are compared with the views only when that happened.
            var simulationManager = _simulationWorld.EntityManager;
            var version = simulationManager.GlobalSystemVersion;
            var isScanRequired = _isScanRequired || _manager.IsScanRequested;
            if (!isScanRequired && version == _scannedVersion)
            {
                return;
            }
            var keyOrderVersion = simulationManager.GetComponentOrderVersion<EntityViewKey>();
            if (isScanRequired || keyOrderVersion != _keyOrderVersion || HaveKeysChanged(simulationManager))
            {
                Scan(simulation.Tick - 1);
            }
            _manager.DetachMissing(simulationManager);
            _keyOrderVersion = keyOrderVersion;
            _scannedVersion = version;
            _isScanRequired = false;
            _manager.ClearScanRequest();
        }

        /// <summary>A key was written since the last comparison (creations and removals show in the order version).</summary>
        private bool HaveKeysChanged(EntityManager simulationManager)
        {
            // One below: writes made outside systems after the last comparison carry exactly that version.
            var baseline = _scannedVersion - 1;
            var keyHandle = simulationManager.GetComponentTypeHandle<EntityViewKey>(true);
            using (var chunks = _keyQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (var i = 0; i < chunks.Length; i++)
                {
                    if (chunks[i].DidChange(ref keyHandle, baseline))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void Scan(int lastTick)
        {
            _scanStamp++;
            _toSpawn.Clear();
            var entities = _keyQuery.ToEntityArray(Allocator.Temp);
            var keys = _keyQuery.ToComponentDataArray<EntityViewKey>(Allocator.Temp);
            var views = _manager.SpawnedViews;
            for (var i = 0; i < entities.Length; i++)
            {
                var key = keys[i].value;
                _prefabs.TryGetValue(key, out var prefab);
                if (views.TryGetValue(entities[i], out var view) && view != null && view.Key == key && view.Prefab == prefab)
                {
                    view.ScanStamp = _scanStamp;
                    continue;
                }
                if (prefab != null)
                {
                    _toSpawn.Add((entities[i], prefab, key));
                }
            }

            // Return first, then spawn: a view released here serves a new entity in the same frame.
            _toRelease.Clear();
            foreach (var pair in views)
            {
                if (pair.Value == null || pair.Value.ScanStamp != _scanStamp)
                {
                    _toRelease.Add(pair.Key);
                }
            }
            for (var i = 0; i < _toRelease.Count; i++)
            {
                Release(_toRelease[i]);
            }
            for (var i = 0; i < _toSpawn.Count; i++)
            {
                var (entity, prefab, key) = _toSpawn[i];
                Spawn(entity, prefab, key, lastTick);
            }
        }

        private void Spawn(Entity entity, EntityView prefab, in FixedString32Bytes key, int lastTick)
        {
            var view = Pool.Spawn(prefab);
            if (view == null)
            {
                Debug.LogError($"[Lockstep] The view pool returned nothing for '{prefab.name}' (key '{key}').", prefab);
                return;
            }
            view.Prefab = prefab;
            view.Key = key;
            view.ScanStamp = _scanStamp;
            _manager.AddSpawned(entity, view, lastTick);
        }

        private void Release(Entity entity)
        {
            if (_manager.RemoveSpawned(entity, out var view) && view != null)
            {
                Pool.Release(view);
            }
        }

        private void ReleaseAll()
        {
            if (_manager.SpawnedViews.Count == 0)
            {
                return;
            }
            // Its own list: a part may swap the pool from BindBreak while a scan is releasing views.
            var entities = new List<Entity>(_manager.SpawnedViews.Keys);
            for (var i = 0; i < entities.Count; i++)
            {
                Release(entities[i]);
            }
        }

        private void EndSession()
        {
            ReleaseAll();
            _manager.DetachAll();
            _manager.SetClient(null);
            _simulationWorld = null;
        }

        private void DisposeOwnedPool()
        {
            if (_isPoolOwned && _pool is IDisposable disposable)
            {
                disposable.Dispose();
            }
            _pool = null;
            _isPoolOwned = false;
        }

        private bool HasCatalogChanged()
        {
            var hasChanged = false;
            if (EntityViewConfigs.Version != _configsVersion)
            {
                _configsVersion = EntityViewConfigs.Version;
                hasChanged = true;
            }
            var orderVersion = EntityManager.GetComponentOrderVersion<EntityViewPrefabElement>();
            if (orderVersion != _registryOrderVersion)
            {
                _registryOrderVersion = orderVersion;
                hasChanged = true;
            }
            if (hasChanged || _registryQuery.IsEmptyIgnoreFilter)
            {
                return hasChanged;
            }

            // A registry edited in place, by live baking in the editor.
            var elementHandle = GetBufferTypeHandle<EntityViewPrefabElement>(true);
            using (var chunks = _registryQuery.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (var i = 0; i < chunks.Length; i++)
                {
                    if (chunks[i].DidChange(ref elementHandle, LastSystemVersion))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void RebuildCatalog()
        {
            _prefabs.Clear();
            if (!_registryQuery.IsEmptyIgnoreFilter)
            {
                var registries = _registryQuery.ToEntityArray(Allocator.Temp);
                for (var r = 0; r < registries.Length; r++)
                {
                    var elements = EntityManager.GetBuffer<EntityViewPrefabElement>(registries[r], true);
                    for (var i = 0; i < elements.Length; i++)
                    {
                        var prefab = elements[i].prefab.Value;
                        var view = prefab != null ? prefab.GetComponent<EntityView>() : null;
                        if (view == null)
                        {
                            Debug.LogError($"[Lockstep] The view prefab of key '{elements[i].key}' is missing or has no EntityView.", prefab);
                            continue;
                        }
                        AddPrefab(elements[i].key, view, prefab);
                    }
                }
            }

            var configs = EntityViewConfigs.All;
            for (var c = 0; c < configs.Count; c++)
            {
                var config = configs[c];
                if (config == null)
                {
                    continue;
                }
                foreach (var binder in config.Binders)
                {
                    if (binder.View == null || !EntityViewKey.TryCreate(binder.Key, out var key))
                    {
                        Debug.LogError($"[Lockstep] {config.name}: the binder '{binder.Key}' needs a view and a key of 1 to 29 bytes.", config);
                        continue;
                    }
                    AddPrefab(key.value, binder.View, config);
                }
            }
        }

        private void AddPrefab(in FixedString32Bytes key, EntityView view, Object context)
        {
            if (!_prefabs.TryGetValue(key, out var existing))
            {
                _prefabs.Add(key, view);
                return;
            }
            if (existing != view)
            {
                Debug.LogWarning($"[Lockstep] The view key '{key}' is bound twice: '{existing.name}' is used, '{view.name}' is ignored.", context);
            }
        }

        // A factory from the previous play session would capture objects that no longer exist.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PoolFactory = null;
        }
    }
}
