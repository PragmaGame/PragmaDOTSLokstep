using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace Pragma.Lockstep
{
    /// <summary>
    /// Mirrors the simulation into the presentation world: for every simulation entity with a
    /// <see cref="LockstepPrefabId"/> it instantiates the registry prefab with the same index, writes an interpolated
    /// <c>LocalTransform</c> from <see cref="LockstepTransform"/> every frame, and destroys the view when the simulation entity goes away.
    /// </summary>
    /// <remarks>
    /// The view prefab is the same baked prefab the simulation instantiates, with its rendering components; in the
    /// presentation world its gameplay components are inert copies. Views are keyed by entity (index and version), so a
    /// recycled simulation entity gets a fresh view.
    /// </remarks>
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
    public partial class LockstepViewSystem : SystemBase
    {
        private NativeHashMap<Entity, Entity> _views;
        // Last simulated tick when each view was created: a view snaps until the next tick, then interpolates.
        private NativeHashMap<Entity, int> _createdAtTick;
        private NativeHashSet<Entity> _alive;
        private NativeList<Entity> _stale;
        private EntityQuery _registryQuery;
        private World _simulationWorld;
        private EntityQuery _simulationQuery;

        /// <summary>Number of live views, mostly for tests and debugging.</summary>
        public int ViewCount => _views.Count;

        public bool TryGetView(Entity simulationEntity, out Entity view) => _views.TryGetValue(simulationEntity, out view);

        protected override void OnCreate()
        {
            _views = new NativeHashMap<Entity, Entity>(256, Allocator.Persistent);
            _createdAtTick = new NativeHashMap<Entity, int>(256, Allocator.Persistent);
            _alive = new NativeHashSet<Entity>(256, Allocator.Persistent);
            _stale = new NativeList<Entity>(64, Allocator.Persistent);
            _registryQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<LockstepPrefabRegistry, LockstepPrefabElement>()
                .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)
                .Build(this);
        }

        protected override void OnDestroy()
        {
            _views.Dispose();
            _createdAtTick.Dispose();
            _alive.Dispose();
            _stale.Dispose();
        }

        protected override void OnUpdate()
        {
            LockstepWorlds.TryGetClient(World, out var client);
            var simulation = client?.Simulation;
            if (simulation == null || !simulation.World.IsCreated)
            {
                DestroyAllViews();
                _simulationWorld = null;
                return;
            }

            if (simulation.World != _simulationWorld)
            {
                DestroyAllViews();
                _simulationWorld = simulation.World;
                _simulationQuery = _simulationWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<LockstepPrefabId>());
            }

            if (_registryQuery.CalculateEntityCount() == 0)
            {
                return;
            }
            var registry = EntityManager.GetBuffer<LockstepPrefabElement>(_registryQuery.GetSingletonEntity(), true).ToNativeArray(Allocator.Temp);

            var simulationManager = _simulationWorld.EntityManager;
            var lastTick = simulation.Tick - 1;
            var alpha = client.InterpolationAlpha;
            var entities = _simulationQuery.ToEntityArray(Allocator.Temp);
            var prefabIds = _simulationQuery.ToComponentDataArray<LockstepPrefabId>(Allocator.Temp);

            _alive.Clear();
            for (var i = 0; i < entities.Length; i++)
            {
                var simulationEntity = entities[i];
                if (!_views.TryGetValue(simulationEntity, out var view))
                {
                    var prefabId = prefabIds[i].value;
                    if (prefabId < 0 || prefabId >= registry.Length || registry[prefabId].prefab == Entity.Null)
                    {
                        continue;
                    }
                    view = EntityManager.Instantiate(registry[prefabId].prefab);
                    EntityManager.AddComponentData(view, new LockstepView { simulationEntity = simulationEntity });
                    _views.Add(simulationEntity, view);
                    _createdAtTick[simulationEntity] = lastTick;
                }
                _alive.Add(simulationEntity);

                if (!simulationManager.HasComponent<LockstepTransform>(simulationEntity) || !EntityManager.HasComponent<LocalTransform>(view))
                {
                    continue;
                }
                var current = simulationManager.GetComponentData<LockstepTransform>(simulationEntity);
                var canInterpolate = _createdAtTick.TryGetValue(simulationEntity, out var createdAt) && lastTick > createdAt;
                var transform = canInterpolate && simulationManager.HasComponent<LockstepTransformPrevious>(simulationEntity)
                    ? LockstepTransformExtensions.Interpolate(current, simulationManager.GetComponentData<LockstepTransformPrevious>(simulationEntity), lastTick, alpha)
                    : current.ToLocalTransform();
                EntityManager.SetComponentData(view, transform);
            }

            _stale.Clear();
            foreach (var pair in _views)
            {
                if (!_alive.Contains(pair.Key))
                {
                    _stale.Add(pair.Key);
                }
            }
            for (var i = 0; i < _stale.Length; i++)
            {
                if (_views.TryGetValue(_stale[i], out var view) && EntityManager.Exists(view))
                {
                    EntityManager.DestroyEntity(view);
                }
                _views.Remove(_stale[i]);
                _createdAtTick.Remove(_stale[i]);
            }
        }

        private void DestroyAllViews()
        {
            if (_views.Count == 0)
            {
                return;
            }
            foreach (var pair in _views)
            {
                if (EntityManager.Exists(pair.Value))
                {
                    EntityManager.DestroyEntity(pair.Value);
                }
            }
            _views.Clear();
            _createdAtTick.Clear();
        }
    }
}
