using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Authoring
{
    /// <summary>
    /// Lists the prefabs the simulation can instantiate. Put it in a subscene loaded by the client (or offline) world:
    /// the registry is copied into every simulation world, and the same list spawns the views.
    /// </summary>
    /// <remarks>The index in the list is the prefab id. Only append to it once a game has shipped, or old replays break.</remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Pragma/Lockstep/Lockstep Prefab Registry")]
    public sealed class LockstepPrefabRegistryAuthoring : MonoBehaviour
    {
        [SerializeField] private List<GameObject> _prefabs = new List<GameObject>();

        private sealed class Baker : Baker<LockstepPrefabRegistryAuthoring>
        {
            public override void Bake(LockstepPrefabRegistryAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent<LockstepPrefabRegistry>(entity);
                var prefabs = AddBuffer<LockstepPrefabElement>(entity);
                foreach (var prefab in authoring._prefabs)
                {
                    prefabs.Add(new LockstepPrefabElement
                    {
                        prefab = prefab != null ? GetEntity(prefab, TransformUsageFlags.Dynamic) : Entity.Null,
                    });
                }
            }
        }
    }
}
