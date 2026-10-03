using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>Player entity of every slot, a singleton buffer in the simulation world (Entity.Null when empty).</summary>
    [InternalBufferCapacity(0)]
    public struct LockstepPlayerSlot : IBufferElementData
    {
        public Entity player;
    }
}
