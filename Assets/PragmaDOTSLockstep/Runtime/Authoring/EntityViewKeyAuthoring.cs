using Pragma.Lockstep.Views;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Authoring
{
    /// <summary>
    /// Bakes an <see cref="EntityViewKey"/>: entities instantiated from this prefab get the GameObject view bound to the key
    /// in an <see cref="EntityViewConfig"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Pragma/Lockstep/Entity View Key")]
    public sealed class EntityViewKeyAuthoring : MonoBehaviour
    {
        [SerializeField, Tooltip("Key of the EntityViewConfig binder whose view shows this entity; up to 29 bytes of UTF-8.")]
        private string _key;

        private sealed class Baker : Baker<EntityViewKeyAuthoring>
        {
            public override void Bake(EntityViewKeyAuthoring authoring)
            {
                if (!EntityViewKey.TryCreate(authoring._key, out var key))
                {
                    Debug.LogError($"[Lockstep] '{authoring.name}': the view key '{authoring._key}' must be 1 to 29 bytes of UTF-8.", authoring);
                    return;
                }
                AddComponent(GetEntity(TransformUsageFlags.None), key);
            }
        }
    }
}
