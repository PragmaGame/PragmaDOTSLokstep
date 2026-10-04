using System;
using Pragma.Lockstep.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Pragma.Lockstep.Stats
{
    /// <summary>
    /// Reads and changes stats, modifiers and grants. Every method can be called from Burst-compiled code; the ones taking
    /// the game's stat enum convert it to the <c>int</c> id the buffers store.
    /// </summary>
    public static class LockstepStats
    {
        /// <summary>The id a stat, target or other enum value is stored as: its numeric value.</summary>
        public static int Id<TEnum>(TEnum value) where TEnum : unmanaged, Enum
        {
            // An enum is its underlying integer, and its size is a constant to Burst.
            switch (UnsafeUtility.SizeOf<TEnum>())
            {
                case 1:
                    return UnsafeUtility.As<TEnum, byte>(ref value);
                case 2:
                    return UnsafeUtility.As<TEnum, ushort>(ref value);
                case 8:
                    return (int)UnsafeUtility.As<TEnum, long>(ref value);
                default:
                    return UnsafeUtility.As<TEnum, int>(ref value);
            }
        }

        /// <summary>
        /// The stat of <paramref name="type"/>, as of the last <see cref="LockstepStatSystem"/> update: its
        /// <see cref="LockstepStat.value"/>, and for a capped resource its <see cref="LockstepStat.max"/>. False when the
        /// entity does not have it.
        /// </summary>
        public static bool TryGet(this DynamicBuffer<LockstepStat> stats, int type, out LockstepStat stat)
        {
            var index = IndexOf(stats, type);
            stat = index >= 0 ? stats[index] : default;
            return index >= 0;
        }

        /// <inheritdoc cref="TryGet(DynamicBuffer{LockstepStat}, int, out LockstepStat)"/>
        public static bool TryGet<TType>(this DynamicBuffer<LockstepStat> stats, TType type, out LockstepStat stat) where TType : unmanaged, Enum
        {
            return TryGet(stats, Id(type), out stat);
        }

        /// <summary>
        /// The value of an attribute with its modifiers applied, or the amount of a resource, as of the last
        /// <see cref="LockstepStatSystem"/> update; false when the entity does not have the stat.
        /// </summary>
        public static bool TryGetValue(this DynamicBuffer<LockstepStat> stats, int type, out FixedPoint value)
        {
            var isFound = TryGet(stats, type, out var stat);
            value = stat.value;
            return isFound;
        }

        /// <inheritdoc cref="TryGetValue(DynamicBuffer{LockstepStat}, int, out FixedPoint)"/>
        public static bool TryGetValue<TType>(this DynamicBuffer<LockstepStat> stats, TType type, out FixedPoint value) where TType : unmanaged, Enum
        {
            return TryGetValue(stats, Id(type), out value);
        }

        /// <summary>The value of an attribute without modifiers; false when the entity does not have the attribute.</summary>
        public static bool TryGetBase(this DynamicBuffer<LockstepStat> stats, int type, out FixedPoint baseValue)
        {
            var isFound = TryGet(stats, type, out var stat) && stat.IsAttribute;
            baseValue = isFound ? stat.baseValue : FixedPoint.Zero;
            return isFound;
        }

        /// <inheritdoc cref="TryGetBase(DynamicBuffer{LockstepStat}, int, out FixedPoint)"/>
        public static bool TryGetBase<TType>(this DynamicBuffer<LockstepStat> stats, TType type, out FixedPoint baseValue) where TType : unmanaged, Enum
        {
            return TryGetBase(stats, Id(type), out baseValue);
        }

        /// <summary>
        /// Changes the base value of an attribute the entity has (a level up, a permanent pickup); false when it does not
        /// have the attribute. The value follows in the next <see cref="LockstepStatSystem"/> update. Resources change with
        /// <see cref="LockstepStatChange"/>s.
        /// </summary>
        public static bool TrySetBase(this DynamicBuffer<LockstepStat> stats, int type, FixedPoint baseValue)
        {
            var index = IndexOf(stats, type);
            if (index < 0 || !stats[index].IsAttribute)
            {
                return false;
            }
            var stat = stats[index];
            stat.baseValue = baseValue;
            stats[index] = stat;
            return true;
        }

        /// <inheritdoc cref="TrySetBase(DynamicBuffer{LockstepStat}, int, FixedPoint)"/>
        public static bool TrySetBase<TType>(this DynamicBuffer<LockstepStat> stats, TType type, FixedPoint baseValue) where TType : unmanaged, Enum
        {
            return TrySetBase(stats, Id(type), baseValue);
        }

        /// <summary>
        /// Removes every modifier of <paramref name="source"/>, keeping the order of the others, and returns how many there
        /// were. Removing a source before adding its modifiers again refreshes an effect instead of stacking it.
        /// </summary>
        public static int RemoveModifiers(this DynamicBuffer<LockstepStatModifier> modifiers, LockstepStatSource source)
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
        /// Removes every grant whose modifier comes from <paramref name="source"/>, keeping the order of the others, and
        /// returns how many there were: the receivers lose them on the same tick.
        /// </summary>
        public static int RemoveGrants(this DynamicBuffer<LockstepStatGrant> grants, LockstepStatSource source)
        {
            var count = 0;
            for (var i = 0; i < grants.Length; i++)
            {
                var grant = grants[i];
                if (grant.modifier.source != source)
                {
                    grants[count++] = grant;
                }
            }
            var removed = grants.Length - count;
            grants.ResizeUninitialized(count);
            return removed;
        }

        /// <summary>
        /// The value an attribute with <paramref name="baseValue"/> gets from <paramref name="modifiers"/>, as
        /// <see cref="LockstepStatSystem"/> computes it: for tooltips and previews. Modifiers of other stats are skipped and
        /// end ticks are not checked; a <see cref="LockstepStatStacking.Strongest"/> modifier counts only when no other one
        /// of its group in the array beats it. The percentage factors never go below zero; the value itself may.
        /// </summary>
        public static FixedPoint Calculate(int type, FixedPoint baseValue, NativeArray<LockstepStatModifier> modifiers)
        {
            var flat = FixedPoint.Zero;
            var additive = FixedPoint.Zero;
            var multiplier = FixedPoint.One;
            for (var i = 0; i < modifiers.Length; i++)
            {
                var modifier = modifiers[i];
                if (modifier.stat != type || !IsApplied(modifiers, i))
                {
                    continue;
                }
                switch (modifier.type)
                {
                    case LockstepStatModifierType.Flat:
                        flat += modifier.value;
                        break;
                    case LockstepStatModifierType.AdditivePercent:
                        additive += modifier.value;
                        break;
                    case LockstepStatModifierType.MultiplicativePercent:
                        multiplier *= Factor(modifier.value);
                        break;
                }
            }
            return (baseValue + flat) * Factor(additive) * multiplier;
        }

        // -100 % or less leaves nothing. A negative factor would flip the sign of the value, and two of them would cancel out
        // into a buff. The flat part may still go below zero: negative armor or regeneration is a legitimate value.
        private static FixedPoint Factor(FixedPoint percent) => FixedMath.Max(FixedPoint.One + percent, FixedPoint.Zero);

        // A non-stacking modifier applies unless another one of its group is stronger, or as strong and earlier.
        private static bool IsApplied(NativeArray<LockstepStatModifier> modifiers, int index)
        {
            var modifier = modifiers[index];
            if (modifier.stacking == LockstepStatStacking.Stack)
            {
                return true;
            }
            var strength = FixedMath.Abs(modifier.value);
            for (var i = 0; i < modifiers.Length; i++)
            {
                var other = modifiers[i];
                if (i == index || other.stacking != LockstepStatStacking.Strongest || other.stat != modifier.stat || other.type != modifier.type ||
                    other.source.kind != modifier.source.kind)
                {
                    continue;
                }
                var otherStrength = FixedMath.Abs(other.value);
                if (otherStrength > strength || (otherStrength == strength && i < index))
                {
                    return false;
                }
            }
            return true;
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
