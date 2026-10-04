using System;
using Unity.Entities;

namespace Pragma.Lockstep.Stats
{
    /// <summary>
    /// A group the entity belongs to for <see cref="LockstepStatGrant"/>s: its type, its category (infantry, vehicles,
    /// buildings). A grant reaches the entity when its <see cref="LockstepStatGrant.target"/> is one of these or
    /// <see cref="LockstepStatGrant.ANY"/>. The game names the groups, usually with an enum; 0 is not one.
    /// </summary>
    public struct LockstepStatTarget : IBufferElementData
    {
        public int value;

        public LockstepStatTarget(int value)
        {
            this.value = value;
        }

        /// <inheritdoc cref="LockstepStatTarget"/>
        public static LockstepStatTarget Create<TTarget>(TTarget target) where TTarget : unmanaged, Enum
        {
            return new LockstepStatTarget(LockstepStats.Id(target));
        }
    }
}
