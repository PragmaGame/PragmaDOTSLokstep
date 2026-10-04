using System;
using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Stats
{
    /// <summary>
    /// A one-tick change of a resource: damage, healing, morale damage, income, a payment. Systems that update before
    /// <see cref="LockstepStatSystem"/> add it to the entity's buffer, and the system applies it on the same tick and
    /// empties the buffer.
    /// </summary>
    /// <remarks>
    /// The changes of a resource on one tick are summed before the cap and the zero floor apply, so the result does not
    /// depend on the order they were added in. A change of an attribute, or of a stat the entity does not have, changes
    /// nothing: attributes change with modifiers and base values. Until the stat system runs, the buffer lists the tick's
    /// changes with their sources, for whatever needs to know who dealt the damage.
    /// </remarks>
    public struct LockstepStatChange : IBufferElementData
    {
        /// <summary>The <see cref="LockstepStat.type"/> of the resource it changes.</summary>
        public int stat;
        /// <summary>Added to the amount: negative for damage and payments, positive for healing and income.</summary>
        public FixedPoint amount;
        public LockstepStatSource source;

        public static LockstepStatChange Create(int stat, FixedPoint amount, LockstepStatSource source = default)
        {
            return new LockstepStatChange { stat = stat, amount = amount, source = source };
        }

        /// <inheritdoc cref="Create(int, FixedPoint, LockstepStatSource)"/>
        public static LockstepStatChange Create<TStat>(TStat stat, FixedPoint amount, LockstepStatSource source = default) where TStat : unmanaged, Enum
        {
            return Create(LockstepStats.Id(stat), amount, source);
        }
    }
}
