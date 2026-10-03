using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Authoring
{
    /// <summary>
    /// Gives instances of this prefab a <see cref="LockstepEntityId"/>, the identity commands and game data should use
    /// to refer to the entity on every client.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Pragma/Lockstep/Lockstep Entity Id")]
    public sealed class LockstepEntityIdAuthoring : MonoBehaviour
    {
        private sealed class Baker : Baker<LockstepEntityIdAuthoring>
        {
            public override void Bake(LockstepEntityIdAuthoring authoring)
            {
                AddComponent(GetEntity(TransformUsageFlags.None), new LockstepEntityId());
            }
        }
    }
}
