using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>Copies the baked prefab registry of a presentation world into a simulation world.</summary>
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

        private static void AddUnique(Entity entity, NativeList<Entity> list, NativeHashMap<Entity, int> index)
        {
            if (index.ContainsKey(entity))
            {
                return;
            }
            index.Add(entity, list.Length);
            list.Add(entity);
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
