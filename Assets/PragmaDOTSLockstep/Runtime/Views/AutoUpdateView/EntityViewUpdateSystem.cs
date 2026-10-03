using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// Pushes the values of the simulation component <typeparamref name="TData"/> into the views of the entities that
    /// have it. Declare one empty subclass per component type:
    /// <c>public partial class HealthViewUpdateSystem : EntityViewUpdateSystem&lt;Health&gt; { }</c>.
    /// </summary>
    /// <remarks>
    /// Only the chunks whose <typeparamref name="TData"/> (or an enableable component of the query) changed since the
    /// previous push are read, judged by the change versions of the simulation world, plus the entities whose views were
    /// spawned, attached or forced since then, so a view always starts from the current values. Every write between two
    /// pushes is seen, also the ones made outside systems (the session entity's <see cref="LockstepTime"/>).
    /// </remarks>
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(EntityViewUpdateSystemGroup))]
    public abstract partial class EntityViewUpdateSystem<TData> : SystemBase where TData : unmanaged, IComponentData
    {
        private readonly List<ComponentType> _enableableTypes = new List<ComponentType>();
        private DynamicComponentTypeHandle[] _enableableHandles = Array.Empty<DynamicComponentTypeHandle>();
        private EntityViewManagerSystem _managerSystem;
        private World _simulationWorld;
        private EntityQuery _query;
        private bool _isZeroSized;
        private uint _seenVersion;
        private int _pushedFrame = -1;

        protected override void OnCreate()
        {
            _isZeroSized = TypeManager.IsZeroSized(TypeManager.GetTypeIndex<TData>());
        }

        /// <summary>
        /// Lets a subclass narrow the simulation query, for example to skip entities another system shows. The builder
        /// is returned rather than changed by ref: <see cref="EntityQueryBuilder"/> is a struct with a fluent API.
        /// </summary>
        protected virtual EntityQueryBuilder ConfigureQuery(EntityQueryBuilder builder)
        {
            return builder;
        }

        protected override void OnUpdate()
        {
            if (_managerSystem == null)
            {
                _managerSystem = World.GetExistingSystemManaged<EntityViewManagerSystem>();
                if (_managerSystem == null)
                {
                    return;
                }
            }
            var manager = _managerSystem.Manager;
            var simulation = manager.Simulation;
            if (simulation == null || !simulation.World.IsCreated)
            {
                _simulationWorld = null;
                return;
            }

            var simulationManager = simulation.World.EntityManager;
            // A missed frame means missed pending views: push everything once.
            var isFullPush = manager.FrameIndex != _pushedFrame + 1;
            _pushedFrame = manager.FrameIndex;
            if (simulation.World != _simulationWorld)
            {
                _simulationWorld = simulation.World;
                _query = BuildQuery(simulationManager);
                isFullPush = true;
            }
            if (!isFullPush && manager.Pending.Count == 0 && simulationManager.GlobalSystemVersion == _seenVersion)
            {
                return;
            }
            if (manager.ViewCount > 0)
            {
                // One below the version seen last: every system bumps the global version once more when it finishes, so
                // a write made outside systems after the previous push carries exactly that version.
                Push(manager, simulationManager, isFullPush, _seenVersion - 1);
            }
            _seenVersion = simulationManager.GlobalSystemVersion;
        }

        private void Push(EntityViewManager manager, EntityManager simulationManager, bool isFullPush, uint baseline)
        {
            var dataHandle = simulationManager.GetComponentTypeHandle<TData>(true);
            var entityHandle = simulationManager.GetEntityTypeHandle();
            for (var i = 0; i < _enableableHandles.Length; i++)
            {
                _enableableHandles[i] = simulationManager.GetDynamicComponentTypeHandle(_enableableTypes[i]);
            }

            using (var chunks = _query.ToArchetypeChunkArray(Allocator.Temp))
            {
                for (var c = 0; c < chunks.Length; c++)
                {
                    var chunk = chunks[c];
                    if (!isFullPush && !HasChanged(chunk, ref dataHandle, baseline))
                    {
                        continue;
                    }
                    var entities = chunk.GetNativeArray(entityHandle);
                    var data = _isZeroSized ? default : chunk.GetNativeArray(ref dataHandle);
                    for (var i = 0; i < entities.Length; i++)
                    {
                        if (_enableableTypes.Count > 0 && !_query.Matches(entities[i]))
                        {
                            continue;
                        }
                        manager.PushData(entities[i], _isZeroSized ? default : data[i]);
                    }
                }
            }
            if (isFullPush)
            {
                return;
            }

            var pending = manager.Pending;
            for (var i = 0; i < pending.Count; i++)
            {
                var entity = pending[i];
                if (!simulationManager.Exists(entity) || !_query.Matches(entity))
                {
                    continue;
                }
                // Pushed above already when its chunk changed.
                if (HasChanged(simulationManager.GetChunk(entity), ref dataHandle, baseline))
                {
                    continue;
                }
                manager.PushData(entity, _isZeroSized ? default : simulationManager.GetComponentData<TData>(entity));
            }
        }

        private bool HasChanged(in ArchetypeChunk chunk, ref ComponentTypeHandle<TData> dataHandle, uint baseline)
        {
            if (chunk.DidChange(ref dataHandle, baseline))
            {
                return true;
            }
            // Enabling or disabling a component only bumps the version of that component, and it moves entities in or out
            // of the query.
            for (var i = 0; i < _enableableHandles.Length; i++)
            {
                if (chunk.DidChange(ref _enableableHandles[i], baseline))
                {
                    return true;
                }
            }
            return false;
        }

        private EntityQuery BuildQuery(EntityManager simulationManager)
        {
            var builder = ConfigureQuery(new EntityQueryBuilder(Allocator.Temp).WithAll<TData>());
            var query = builder.Build(simulationManager);
            builder.Dispose();

            // Disabled components hide entities from a query, but chunks are read whole: entities of such queries are
            // checked one by one, and toggling those components counts as a change.
            _enableableTypes.Clear();
            foreach (var desc in query.GetEntityQueryDescs())
            {
                AddEnableable(desc.All);
                AddEnableable(desc.Any);
                AddEnableable(desc.None);
                AddEnableable(desc.Disabled);
                AddEnableable(desc.Present);
                AddEnableable(desc.Absent);
            }
            _enableableHandles = new DynamicComponentTypeHandle[_enableableTypes.Count];
            return query;
        }

        private void AddEnableable(ComponentType[] types)
        {
            if (types == null)
            {
                return;
            }
            for (var i = 0; i < types.Length; i++)
            {
                var type = ComponentType.ReadOnly(types[i].TypeIndex);
                if (type.IsEnableable && !_enableableTypes.Contains(type))
                {
                    _enableableTypes.Add(type);
                }
            }
        }
    }
}
