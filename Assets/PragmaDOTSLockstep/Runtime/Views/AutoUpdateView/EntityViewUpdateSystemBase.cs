using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// What <see cref="EntityViewUpdateSystem{TData}"/> and <see cref="EntityBufferViewUpdateSystem{TElement}"/> share:
    /// finding the entities whose views need their data this frame. Derive from one of those two.
    /// </summary>
    /// <remarks>
    /// Only the chunks whose data (or an enableable component of the query) changed since the previous push are read,
    /// judged by the change versions of the simulation world, plus the entities whose views were spawned, attached or
    /// forced since then, so a view always starts from the current values. Every write between two pushes is seen, also
    /// the ones made outside systems (the session entity's <see cref="LockstepTime"/>).
    /// </remarks>
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(EntityViewUpdateSystemGroup))]
    public abstract partial class EntityViewUpdateSystemBase : SystemBase
    {
        private readonly List<ComponentType> _enableableTypes = new List<ComponentType>();
        private DynamicComponentTypeHandle[] _enableableHandles = Array.Empty<DynamicComponentTypeHandle>();
        private World _simulationWorld;
        private EntityQuery _query;
        private uint _seenVersion;
        private int _pushedFrame = -1;

        /// <summary>The simulation component or buffer element type the views get.</summary>
        private protected abstract ComponentType DataType { get; }

        /// <summary>
        /// Lets a subclass narrow the simulation query, for example to skip entities another system shows. The builder
        /// is returned rather than changed by ref: <see cref="EntityQueryBuilder"/> is a struct with a fluent API.
        /// </summary>
        protected virtual EntityQueryBuilder ConfigureQuery(EntityQueryBuilder builder)
        {
            return builder;
        }

        /// <summary>Takes the type handles of the simulation world for this push.</summary>
        private protected abstract void UpdateHandles(EntityManager simulationManager);

        /// <summary>A changed chunk is about to be pushed entity by entity.</summary>
        private protected abstract void BeginChunk(in ArchetypeChunk chunk);

        /// <summary>Pushes the data of the entity at <paramref name="index"/> of the chunk passed to <see cref="BeginChunk"/>.</summary>
        private protected abstract void PushFromChunk(EntityViewManager manager, int index, Entity entity);

        /// <summary>Pushes the data of an entity whose view is new, read through the entity manager.</summary>
        private protected abstract void PushFromEntity(EntityViewManager manager, EntityManager simulationManager, Entity entity);

        protected override void OnUpdate()
        {
            if (!EntityViewManager.TryGet(World, out var manager))
            {
                return;
            }
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
            var dataHandle = simulationManager.GetDynamicComponentTypeHandle(DataType);
            var entityHandle = simulationManager.GetEntityTypeHandle();
            for (var i = 0; i < _enableableHandles.Length; i++)
            {
                _enableableHandles[i] = simulationManager.GetDynamicComponentTypeHandle(_enableableTypes[i]);
            }
            UpdateHandles(simulationManager);

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
                    BeginChunk(chunk);
                    for (var i = 0; i < entities.Length; i++)
                    {
                        if (_enableableTypes.Count > 0 && !_query.Matches(entities[i]))
                        {
                            continue;
                        }
                        PushFromChunk(manager, i, entities[i]);
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
                PushFromEntity(manager, simulationManager, entity);
            }
        }

        private bool HasChanged(in ArchetypeChunk chunk, ref DynamicComponentTypeHandle dataHandle, uint baseline)
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
            var dataTypes = new FixedList32Bytes<ComponentType> { DataType };
            var builder = ConfigureQuery(new EntityQueryBuilder(Allocator.Temp).WithAll(ref dataTypes));
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
