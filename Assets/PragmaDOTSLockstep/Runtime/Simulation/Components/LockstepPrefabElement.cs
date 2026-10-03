using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// Prefabs the simulation can instantiate, addressed by index. The baked registry of the client world is copied
    /// into every simulation world, so the same index means the same prefab on every client and in the presentation.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct LockstepPrefabElement : IBufferElementData
    {
        public Entity prefab;
    }
}
