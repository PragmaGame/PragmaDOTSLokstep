using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// Copies the baked content of a presentation world into a simulation world: the prefab registry and the scene
    /// entities.
    /// </summary>
    public static class LockstepPrefabUtility
    {
        public static bool HasRegistry(EntityManager entityManager)
        {
            using (var query = CreateRegistryQuery(entityManager))
            {
                return !query.IsEmptyIgnoreFilter;
            }
        }

        /// <summary>
        /// Copies every registry prefab (with its linked entities) into <paramref name="destination"/>, registers them
        /// there under the same indices and tags the copies with <see cref="LockstepPrefabId"/>.
        /// </summary>
        /// <returns>False when the source has no registry; the destination then has an empty registry.</returns>
        public static bool CopyRegistry(EntityManager source, EntityManager destination)
        {
            var sourcePrefabs = new NativeList<Entity>(Allocator.Temp);
            using (var query = CreateRegistryQuery(source))
            {
                var registries = query.ToEntityArray(Allocator.Temp);
                if (registries.Length > 1)
                {
                    UnityEngine.Debug.LogWarning("[Lockstep] More than one prefab registry found; only the first one is used.");
                }
                if (registries.Length > 0)
                {
                    var buffer = source.GetBuffer<LockstepPrefabElement>(registries[0], true);
                    for (var i = 0; i < buffer.Length; i++)
                    {
                        sourcePrefabs.Add(buffer[i].prefab);
                    }
                }
            }

            // Every entity of each prefab group must be copied in one call so internal references get remapped.
            var toCopy = new NativeList<Entity>(Allocator.Temp);
            var copyIndex = new NativeHashMap<Entity, int>(16, Allocator.Temp);
            for (var i = 0; i < sourcePrefabs.Length; i++)
            {
                var prefab = sourcePrefabs[i];
                if (prefab == Entity.Null || !source.Exists(prefab))
                {
                    continue;
                }
                if (source.HasBuffer<LinkedEntityGroup>(prefab))
                {
                    var group = source.GetBuffer<LinkedEntityGroup>(prefab, true);
                    for (var g = 0; g < group.Length; g++)
                    {
                        AddUnique(group[g].Value, toCopy, copyIndex);
                    }
                }
                AddUnique(prefab, toCopy, copyIndex);
            }

            var copies = new NativeArray<Entity>(toCopy.Length, Allocator.Temp);
            if (toCopy.Length > 0)
            {
                destination.CopyEntitiesFrom(source, toCopy.AsArray(), copies);
            }

            var registry = destination.CreateEntity(ComponentType.ReadWrite<LockstepPrefabRegistry>(), ComponentType.ReadWrite<LockstepPrefabElement>());
            destination.SetName(registry, "LockstepPrefabRegistry");
            var roots = new NativeArray<Entity>(sourcePrefabs.Length, Allocator.Temp);
            for (var i = 0; i < sourcePrefabs.Length; i++)
            {
                roots[i] = copyIndex.TryGetValue(sourcePrefabs[i], out var index) ? copies[index] : Entity.Null;
            }

            var elements = destination.GetBuffer<LockstepPrefabElement>(registry);
            for (var i = 0; i < roots.Length; i++)
            {
                elements.Add(new LockstepPrefabElement { prefab = roots[i] });
            }
            for (var i = 0; i < roots.Length; i++)
            {
                if (roots[i] != Entity.Null)
                {
                    destination.AddComponentData(roots[i], new LockstepPrefabId { value = i });
                }
            }
            return sourcePrefabs.Length > 0;
        }

        /// <summary>
        /// Copies every <see cref="LockstepSceneEntity"/> of <paramref name="source"/> (with its linked entities) into
        /// <paramref name="destination"/>, ordered by <see cref="LockstepSceneEntity.order"/>. Prefabs are not scene
        /// entities and are skipped.
        /// </summary>
        /// <remarks>
        /// References between the copied entities are remapped to the copies; references to anything else, registry
        /// prefabs included, become <see cref="Entity.Null"/>, so scene entities name prefabs by registry index or by a key.
        /// </remarks>
        /// <returns>The number of scene entities copied.</returns>
        public static int CopySceneEntities(EntityManager source, EntityManager destination)
        {
            var description = new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<LockstepSceneEntity>() },
                Options = EntityQueryOptions.IncludeDisabledEntities,
            };
            NativeArray<Entity> entities;
            NativeArray<LockstepSceneEntity> markers;
            using (var query = source.CreateEntityQuery(description))
            {
                entities = query.ToEntityArray(Allocator.Temp);
                markers = query.ToComponentDataArray<LockstepSceneEntity>(Allocator.Temp);
            }
            if (entities.Length == 0)
            {
                return 0;
            }

            // The query order follows the order the subscenes loaded in, which may differ between clients.
            var order = new NativeArray<int>(entities.Length, Allocator.Temp);
            for (var i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }
            order.Sort(new SceneEntityComparer { markers = markers });

            var toCopy = new NativeList<Entity>(Allocator.Temp);
            var copyIndex = new NativeHashMap<Entity, int>(entities.Length, Allocator.Temp);
            for (var i = 0; i < order.Length; i++)
            {
                var entity = entities[order[i]];
                AddUnique(entity, toCopy, copyIndex);
                if (source.HasBuffer<LinkedEntityGroup>(entity))
                {
                    var group = source.GetBuffer<LinkedEntityGroup>(entity, true);
                    for (var g = 0; g < group.Length; g++)
                    {
                        AddUnique(group[g].Value, toCopy, copyIndex);
                    }
                }
            }
            destination.CopyEntitiesFrom(source, toCopy.AsArray());
            return entities.Length;
        }

        private static void AddUnique(Entity entity, NativeList<Entity> list, NativeHashMap<Entity, int> index)
        {
            if (index.ContainsKey(entity))
            {
                return;
            }
            index.Add(entity, list.Length);
            list.Add(entity);
        }

        // By the baked order; equal orders keep the query order rather than whatever an unstable sort leaves.
        private struct SceneEntityComparer : IComparer<int>
        {
            public NativeArray<LockstepSceneEntity> markers;

            public int Compare(int x, int y)
            {
                var result = markers[x].order.CompareTo(markers[y].order);
                return result != 0 ? result : x.CompareTo(y);
            }
        }

        private static EntityQuery CreateRegistryQuery(EntityManager entityManager)
        {
            return entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<LockstepPrefabRegistry>(), ComponentType.ReadOnly<LockstepPrefabElement>() },
                Options = EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities,
            });
        }
    }
}
