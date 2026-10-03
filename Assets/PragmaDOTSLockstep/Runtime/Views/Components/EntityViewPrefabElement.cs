using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Views
{
    /// <summary>One binder of a baked <see cref="EntityViewConfig"/>: the view prefab spawned for entities with the key.</summary>
    /// <remarks>An unmanaged reference, so the catalog needs no managed component.</remarks>
    [InternalBufferCapacity(0)]
    public struct EntityViewPrefabElement : IBufferElementData
    {
        public FixedString32Bytes key;
        public UnityObjectRef<GameObject> prefab;
    }
}
