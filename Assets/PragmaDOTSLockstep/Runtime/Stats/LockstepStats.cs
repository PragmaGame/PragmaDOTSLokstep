using Pragma.Lockstep.Mathematics;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep.Stats
{
    /// <summary>Reads and changes stats and modifiers. Every method can be called from Burst-compiled code.</summary>
    public static class LockstepStats
    {
        /// <summary>
        /// The value of the stat with its modifiers applied, as of the last <see cref="LockstepStatSystem"/> update; false
        /// when the entity does not have the stat.
        /// </summary>
        public static bool TryGetValue(in DynamicBuffer<LockstepStat> stats, int type, out FixedPoint value)
        {
            var index = IndexOf(stats, type);
            value = index >= 0 ? stats[index].value : FixedPoint.Zero;
            return index >= 0;
        }

        /// <summary>The value of the stat without modifiers; false when the entity does not have the stat.</summary>
        public static bool TryGetBase(in DynamicBuffer<LockstepStat> stats, int type, out FixedPoint baseValue)
        {
            var index = IndexOf(stats, type);
            baseValue = index >= 0 ? stats[index].baseValue : FixedPoint.Zero;
            return index >= 0;
        }

        /// <summary>
        /// Changes the base value of a stat the entity has (a level up, a permanent pickup); false when it does not have the
        /// stat. The value follows in the next <see cref="LockstepStatSystem"/> update.
        /// </summary>
        public static bool TrySetBase(DynamicBuffer<LockstepStat> stats, int type, FixedPoint baseValue)
        {
            var index = IndexOf(stats, type);
            if (index < 0)
            {
                return false;
            }
            var stat = stats[index];
            stat.baseValue = baseValue;
            stats[index] = stat;
            return true;
        }

        /// <summary>
        /// Removes every modifier of <paramref name="source"/>, keeping the order of the others, and returns how many there
        /// were. Removing a source before adding its modifiers again refreshes an effect instead of stacking it.
        /// </summary>
        public static int RemoveModifiers(DynamicBuffer<LockstepStatModifier> modifiers, LockstepStatSource source)
        {
            var count = 0;
            for (var i = 0; i < modifiers.Length; i++)
            {
                var modifier = modifiers[i];
                if (modifier.source != source)
                {
                    modifiers[count++] = modifier;
                }
            }
            var removed = modifiers.Length - count;
            modifiers.ResizeUninitialized(count);
            return removed;
        }

        /// <summary>
        /// The value a stat with <paramref name="baseValue"/> gets from <paramref name="modifiers"/>, as
        /// <see cref="LockstepStatSystem"/> computes it: for tooltips and previews. Modifiers of other stats are skipped;
        /// end ticks are not checked.
        /// </summary>
        public static FixedPoint Calculate(int type, FixedPoint baseValue, NativeArray<LockstepStatModifier> modifiers)
        {
            var flat = FixedPoint.Zero;
            var additive = FixedPoint.Zero;
            var multiplier = FixedPoint.One;
            for (var i = 0; i < modifiers.Length; i++)
            {
                var modifier = modifiers[i];
                if (modifier.stat != type)
                {
                    continue;
                }
                switch (modifier.type)
                {
                    case LockstepStatModifierType.Flat:
                        flat += modifier.value;
                        break;
                    case LockstepStatModifierType.Additive:
                        additive += modifier.value;
                        break;
                    case LockstepStatModifierType.Multiplicative:
                        multiplier *= FixedPoint.One + modifier.value;
                        break;
                }
            }
            return (baseValue + flat) * (FixedPoint.One + additive) * multiplier;
        }

        private static int IndexOf(in DynamicBuffer<LockstepStat> stats, int type)
        {
            for (var i = 0; i < stats.Length; i++)
            {
                if (stats[i].type == type)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
