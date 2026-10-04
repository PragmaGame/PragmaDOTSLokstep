namespace Pragma.Lockstep.Stats
{
    /// <summary>What a <see cref="LockstepStat"/> holds, and so what changes it.</summary>
    public enum LockstepStatKind : byte
    {
        /// <summary>
        /// A number of the entity (max health, speed, damage, range): a base value with the
        /// <see cref="LockstepStatModifier"/>s and the inherited <see cref="LockstepStatGrant"/>s applied.
        /// </summary>
        Attribute,
        /// <summary>
        /// An amount gameplay spends and refills (health, morale, energy, a player's money): changed by
        /// <see cref="LockstepStatChange"/>s, never below zero, optionally capped by an attribute.
        /// </summary>
        Resource,
    }
}
