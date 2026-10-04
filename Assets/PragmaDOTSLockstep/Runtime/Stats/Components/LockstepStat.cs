using System;
using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Stats
{
    /// <summary>
    /// One stat of an entity: an attribute (max health, speed, damage, sight) or a resource (health, morale, a player's
    /// money). The buffer holds each stat of the entity once.
    /// </summary>
    /// <remarks>
    /// The game names its stats: <see cref="type"/> is an id of its own, usually an enum value (the generic overloads take
    /// the enum itself), and 0 is <see cref="NONE"/>. <see cref="LockstepStatSystem"/> keeps <see cref="value"/> up to
    /// date: attributes from their base value and modifiers, resources from their changes and cap. Gameplay reads it with
    /// <see cref="LockstepStats.TryGet(DynamicBuffer{LockstepStat}, int, out LockstepStat)"/> after that system.
    /// </remarks>
    public struct LockstepStat : IBufferElementData
    {
        /// <summary>No stat: the <see cref="cap"/> of an uncapped resource. Never a <see cref="type"/>.</summary>
        public const int NONE = 0;

        /// <summary>The game's id of the stat.</summary>
        public int type;
        public LockstepStatKind kind;
        /// <summary>Resources: what the amount does when the cap changes.</summary>
        public LockstepStatCapPolicy capPolicy;
        /// <summary>Resources: the <see cref="type"/> of the attribute of the same entity that caps the amount, or <see cref="NONE"/>.</summary>
        public int cap;
        /// <summary>Attributes: the value without modifiers, authored or changed with <see cref="LockstepStats.TrySetBase(DynamicBuffer{LockstepStat}, int, FixedPoint)"/>.</summary>
        public FixedPoint baseValue;
        /// <summary>Attributes: the base value with the modifiers applied. Resources: the amount.</summary>
        public FixedPoint value;
        /// <summary>
        /// Capped resources: the cap the amount was last fitted to, as of the last <see cref="LockstepStatSystem"/> update,
        /// which is the maximum a bar shows. Zero until the first update, which fills the resource.
        /// </summary>
        public FixedPoint max;

        public bool IsAttribute => kind == LockstepStatKind.Attribute;

        public bool IsResource => kind == LockstepStatKind.Resource;

        /// <summary>An attribute without modifiers yet: its value is the base value.</summary>
        public static LockstepStat Attribute(int type, FixedPoint baseValue)
        {
            return new LockstepStat { type = type, kind = LockstepStatKind.Attribute, baseValue = baseValue, value = baseValue };
        }

        /// <inheritdoc cref="Attribute(int, FixedPoint)"/>
        public static LockstepStat Attribute<TType>(TType type, FixedPoint baseValue) where TType : unmanaged, Enum
        {
            return Attribute(LockstepStats.Id(type), baseValue);
        }

        /// <summary>An uncapped resource holding <paramref name="amount"/>, like a player's money.</summary>
        public static LockstepStat Resource(int type, FixedPoint amount)
        {
            return new LockstepStat { type = type, kind = LockstepStatKind.Resource, value = amount };
        }

        /// <inheritdoc cref="Resource(int, FixedPoint)"/>
        public static LockstepStat Resource<TType>(TType type, FixedPoint amount) where TType : unmanaged, Enum
        {
            return Resource(LockstepStats.Id(type), amount);
        }

        /// <summary>
        /// A resource capped by the attribute <paramref name="cap"/> of the same entity, like health by max health. It
        /// starts full: the first <see cref="LockstepStatSystem"/> update fills it to the cap, modifiers included.
        /// </summary>
        public static LockstepStat Resource(int type, int cap, LockstepStatCapPolicy capPolicy)
        {
            return new LockstepStat { type = type, kind = LockstepStatKind.Resource, cap = cap, capPolicy = capPolicy };
        }

        /// <inheritdoc cref="Resource(int, int, LockstepStatCapPolicy)"/>
        public static LockstepStat Resource<TType>(TType type, TType cap, LockstepStatCapPolicy capPolicy) where TType : unmanaged, Enum
        {
            return Resource(LockstepStats.Id(type), LockstepStats.Id(cap), capPolicy);
        }
    }
}
