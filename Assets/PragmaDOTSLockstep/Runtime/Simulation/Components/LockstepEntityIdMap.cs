using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// Id to entity lookup rebuilt at the end of every tick, a singleton in the simulation world. Derived data, so it is
    /// left out of the checksum.
    /// </summary>
    [LockstepChecksumIgnore]
    public struct LockstepEntityIdMap : IComponentData
    {
        public NativeHashMap<uint, Entity> map;

        public bool TryGetEntity(uint id, out Entity entity) => map.TryGetValue(id, out entity);

        public bool TryGetEntity(LockstepEntityId id, out Entity entity) => map.TryGetValue(id.value, out entity);
    }
}
