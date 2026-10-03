using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Stats
{
    /// <summary>
    /// One stat of an entity (health, speed, damage, sight): its base value and the value with the entity's
    /// <see cref="LockstepStatModifier"/>s applied. The buffer holds each stat of the entity once.
    /// </summary>
    /// <remarks>
    /// The game names its stats: <see cref="type"/> is an id of its own, usually an enum cast to <c>int</c>. Gameplay reads
    /// <see cref="value"/> (<see cref="LockstepStats.TryGetValue"/>), and <see cref="LockstepStatSystem"/> recalculates it
    /// when the base value or the modifiers change. An entity with stats also needs the <see cref="LockstepStatModifier"/>
    /// buffer, empty or not: the system updates only entities that have both.
    /// </remarks>
    public struct LockstepStat : IBufferElementData
    {
        /// <summary>The game's id of the stat.</summary>
        public int type;
        /// <summary>The value without modifiers: authored, or changed by gameplay with <see cref="LockstepStats.TrySetBase"/>.</summary>
        public FixedPoint baseValue;
        /// <summary>The base value with every modifier of the stat applied, as of the last <see cref="LockstepStatSystem"/> update.</summary>
        public FixedPoint value;

        /// <summary>A stat without modifiers yet: its value is the base value.</summary>
        public static LockstepStat Create(int type, FixedPoint baseValue)
        {
            return new LockstepStat { type = type, baseValue = baseValue, value = baseValue };
        }
    }
}
