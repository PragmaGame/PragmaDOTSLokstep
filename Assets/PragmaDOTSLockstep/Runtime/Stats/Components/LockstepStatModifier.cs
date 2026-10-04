using System;
using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Stats
{
    /// <summary>
    /// A change to one attribute of its entity: an upgrade, a research, an ability, an aura, cover. The entity keeps its
    /// modifiers in this buffer next to its <see cref="LockstepStat"/>s, and <see cref="LockstepStatSystem"/> applies them;
    /// modifiers for a whole group go to the group's <see cref="LockstepStatGrant"/>s instead.
    /// </summary>
    /// <remarks>
    /// Add and remove modifiers in systems that update before <see cref="LockstepStatSystem"/>, and the stats change on the
    /// same tick. A modifier of a resource, or of a stat the entity does not have, changes nothing: resources change with
    /// <see cref="LockstepStatChange"/>s. A timed modifier is removed by itself on its <see cref="endTick"/>; any modifier
    /// is removed with the others of its <see cref="source"/>.
    /// </remarks>
    public struct LockstepStatModifier : IBufferElementData
    {
        /// <summary><see cref="endTick"/> of a modifier that lasts until it is removed.</summary>
        public const int PERMANENT = 0;

        /// <summary>The <see cref="LockstepStat.type"/> of the attribute it changes.</summary>
        public int stat;
        public LockstepStatModifierType type;
        public LockstepStatStacking stacking;
        public FixedPoint value;
        public LockstepStatSource source;
        /// <summary>
        /// The <see cref="LockstepTime.tick"/> the modifier is removed on, so it applies up to the tick before; or
        /// <see cref="PERMANENT"/>. A modifier added on tick T for D ticks ends on T + D.
        /// </summary>
        public int endTick;

        /// <summary>The modifier has ended by <paramref name="tick"/>: a timed one whose end tick has come.</summary>
        public bool IsExpired(int tick) => endTick != PERMANENT && tick >= endTick;

        /// <summary>Adds <paramref name="value"/> to the base of the attribute.</summary>
        public static LockstepStatModifier Flat(int stat, FixedPoint value, LockstepStatSource source = default, int endTick = PERMANENT,
                                                LockstepStatStacking stacking = LockstepStatStacking.Stack)
        {
            return Create(stat, LockstepStatModifierType.Flat, value, source, endTick, stacking);
        }

        /// <inheritdoc cref="Flat(int, FixedPoint, LockstepStatSource, int, LockstepStatStacking)"/>
        public static LockstepStatModifier Flat<TStat>(TStat stat, FixedPoint value, LockstepStatSource source = default, int endTick = PERMANENT,
                                                       LockstepStatStacking stacking = LockstepStatStacking.Stack) where TStat : unmanaged, Enum
        {
            return Flat(LockstepStats.Id(stat), value, source, endTick, stacking);
        }

        /// <summary>
        /// Adds a share of the base and the flat modifiers, summed with the other additive ones: 0.25 adds 25 %, a sum of -1 or
        /// less makes the attribute zero.
        /// </summary>
        public static LockstepStatModifier AdditivePercent(int stat, FixedPoint value, LockstepStatSource source = default, int endTick = PERMANENT,
                                                           LockstepStatStacking stacking = LockstepStatStacking.Stack)
        {
            return Create(stat, LockstepStatModifierType.AdditivePercent, value, source, endTick, stacking);
        }

        /// <inheritdoc cref="AdditivePercent(int, FixedPoint, LockstepStatSource, int, LockstepStatStacking)"/>
        public static LockstepStatModifier AdditivePercent<TStat>(TStat stat, FixedPoint value, LockstepStatSource source = default, int endTick = PERMANENT,
                                                                  LockstepStatStacking stacking = LockstepStatStacking.Stack) where TStat : unmanaged, Enum
        {
            return AdditivePercent(LockstepStats.Id(stat), value, source, endTick, stacking);
        }

        /// <summary>
        /// Scales the attribute by one plus <paramref name="value"/>, on top of the other multiplicative modifiers: 0.5 makes
        /// 1.5 times, -0.5 halves it, -1 or less makes it zero.
        /// </summary>
        public static LockstepStatModifier MultiplicativePercent(int stat, FixedPoint value, LockstepStatSource source = default, int endTick = PERMANENT,
                                                                 LockstepStatStacking stacking = LockstepStatStacking.Stack)
        {
            return Create(stat, LockstepStatModifierType.MultiplicativePercent, value, source, endTick, stacking);
        }

        /// <inheritdoc cref="MultiplicativePercent(int, FixedPoint, LockstepStatSource, int, LockstepStatStacking)"/>
        public static LockstepStatModifier MultiplicativePercent<TStat>(TStat stat, FixedPoint value, LockstepStatSource source = default, int endTick = PERMANENT,
                                                                        LockstepStatStacking stacking = LockstepStatStacking.Stack) where TStat : unmanaged, Enum
        {
            return MultiplicativePercent(LockstepStats.Id(stat), value, source, endTick, stacking);
        }

        private static LockstepStatModifier Create(int stat, LockstepStatModifierType type, FixedPoint value, LockstepStatSource source, int endTick,
                                                   LockstepStatStacking stacking)
        {
            return new LockstepStatModifier { stat = stat, type = type, stacking = stacking, value = value, source = source, endTick = endTick };
        }
    }
}
