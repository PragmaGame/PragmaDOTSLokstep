using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>Index of the registry prefab an entity was instantiated from; the presentation spawns a matching view.</summary>
    public struct LockstepPrefabId : IComponentData
    {
        public int value;
    }
}
