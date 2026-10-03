using Unity.Entities;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pragma.Lockstep.Authoring
{
    /// <summary>
    /// Bakes a <see cref="LockstepSceneEntity"/>: this scene object is copied into every simulation world before tick 0.
    /// Use it for what a map starts with (buildings, resource nodes, spawn markers).
    /// </summary>
    /// <remarks>
    /// Put the object into the subscene that holds the <see cref="LockstepPrefabRegistryAuthoring"/> and start sessions
    /// with <c>waitForPrefabRegistry</c>, so it has loaded when the simulation is created. Give it its simulation data
    /// like any prefab (<see cref="LockstepTransformAuthoring"/>, <see cref="LockstepEntityIdAuthoring"/>,
    /// <see cref="EntityViewKeyAuthoring"/>, your own bakers). Only scene objects are copied, never prefab assets.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Pragma/Lockstep/Lockstep Scene Entity")]
    public sealed class LockstepSceneEntityAuthoring : MonoBehaviour
    {
        private sealed class Baker : Baker<LockstepSceneEntityAuthoring>
        {
            public override void Bake(LockstepSceneEntityAuthoring authoring)
            {
                AddComponent(GetEntity(TransformUsageFlags.None), new LockstepSceneEntity { order = GetOrder(authoring) });
            }
        }

        // The identity of the object in its scene file: the same wherever the scene is baked, unlike an instance id, and
        // unique in the project. Baking only runs in the editor.
        private static ulong GetOrder(LockstepSceneEntityAuthoring authoring)
        {
#if UNITY_EDITOR
            return TypeHash.FNV1A64(GlobalObjectId.GetGlobalObjectIdSlow(authoring.gameObject).ToString());
#else
            return 0;
#endif
        }
    }
}
