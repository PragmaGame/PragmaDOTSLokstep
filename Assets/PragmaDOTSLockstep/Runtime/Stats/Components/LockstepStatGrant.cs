using System;
using Unity.Entities;

namespace Pragma.Lockstep.Stats
{
    /// <summary>
    /// A modifier an entity gives to the entities that inherit from it, those whose <see cref="LockstepStatGrantor"/>
    /// buffer names it: a player's research for all of its units of a type, a squad's ability for every member, a unit's
    /// upgrade for its weapons.
    /// </summary>
    /// <remarks>
    /// Each receiver with a matching <see cref="target"/> applies the modifier as one of its own, stacking rules included,
    /// and so does every entity that starts inheriting later (a new unit, a reinforcement). Removing the grant or its end
    /// tick takes it from all of them on the same tick. Change grants in systems that update before
    /// <see cref="LockstepStatSystem"/>; <see cref="LockstepStats.RemoveGrants"/> removes those of a source.
    /// </remarks>
    public struct LockstepStatGrant : IBufferElementData
    {
        /// <summary><see cref="target"/> of a grant for every receiver.</summary>
        public const int ANY = 0;

        /// <summary>The <see cref="LockstepStatTarget"/> a receiver needs to get the modifier, or <see cref="ANY"/>.</summary>
        public int target;
        public LockstepStatModifier modifier;

        public static LockstepStatGrant Create(LockstepStatModifier modifier, int target = ANY)
        {
            return new LockstepStatGrant { target = target, modifier = modifier };
        }

        /// <inheritdoc cref="Create(LockstepStatModifier, int)"/>
        public static LockstepStatGrant Create<TTarget>(LockstepStatModifier modifier, TTarget target) where TTarget : unmanaged, Enum
        {
            return Create(modifier, LockstepStats.Id(target));
        }
    }
}
