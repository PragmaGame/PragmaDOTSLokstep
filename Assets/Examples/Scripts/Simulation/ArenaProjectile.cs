using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Examples
{
    public struct ArenaProjectile : IComponentData
    {
        public int ownerSlot;
        public FixedVector2 velocity;
        public int ticksLeft;
    }
}
