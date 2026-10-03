using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Stats
{
    /// <summary>
    /// A change to one stat of its entity: an upgrade, a research, an ability, an aura, cover. The entity keeps its modifiers
    /// in this buffer next to its <see cref="LockstepStat"/>s, and <see cref="LockstepStatSystem"/> applies them.
    /// </summary>
    /// <remarks>
    /// Add and remove modifiers in systems that update before <see cref="LockstepStatSystem"/>, and the stats change on the
    /// same tick. A modifier of a stat the entity does not have changes nothing. A timed modifier is removed by itself on its
    /// <see cref="endTick"/>; any modifier is removed with the others of its <see cref="source"/>.
    /// </remarks>
    public struct LockstepStatModifier : IBufferElementData
    {
        /// <summary><see cref="endTick"/> of a modifier that lasts until it is removed.</summary>
        public const int PERMANENT = 0;

        /// <summary>The <see cref="LockstepStat.type"/> it changes.</summary>
        public int stat;
        public LockstepStatModifierType type;
        public FixedPoint value;
        public LockstepStatSource source;
        /// <summary>
        /// The <see cref="LockstepTime.tick"/> the modifier is removed on, so it applies up to the tick before; or
        /// <see cref="PERMANENT"/>. A modifier added on tick T for D ticks ends on T + D.
        /// </summary>
        public int endTick;

        /// <summary>The modifier has ended by <paramref name="tick"/>: a timed one whose end tick has come.</summary>
        public bool IsExpired(int tick) => endTick != PERMANENT && tick >= endTick;

        /// <summary>Adds <paramref name="value"/> to the base of the stat.</summary>
        public static LockstepStatModifier Flat(int stat, FixedPoint value, LockstepStatSource source = default, int endTick = PERMANENT)
        {
            return Create(stat, LockstepStatModifierType.Flat, value, source, endTick);
        }

        /// <summary>Adds a share of the base, summed with the other additive modifiers: 0.25 adds 25 %.</summary>
        public static LockstepStatModifier Additive(int stat, FixedPoint value, LockstepStatSource source = default, int endTick = PERMANENT)
        {
            return Create(stat, LockstepStatModifierType.Additive, value, source, endTick);
        }

        /// <summary>Scales the stat by one plus <paramref name="value"/>, on top of the other multiplicative modifiers.</summary>
        public static LockstepStatModifier Multiplicative(int stat, FixedPoint value, LockstepStatSource source = default, int endTick = PERMANENT)
        {
            return Create(stat, LockstepStatModifierType.Multiplicative, value, source, endTick);
        }

        private static LockstepStatModifier Create(int stat, LockstepStatModifierType type, FixedPoint value, LockstepStatSource source, int endTick)
        {
            return new LockstepStatModifier { stat = stat, type = type, value = value, source = source, endTick = endTick };
        }
    }
}
