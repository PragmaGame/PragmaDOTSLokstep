namespace Pragma.Lockstep.Stats
{
    /// <summary>Whether a <see cref="LockstepStatModifier"/> adds up with the others of the same kind.</summary>
    public enum LockstepStatStacking : byte
    {
        /// <summary>Applies together with every other modifier of the stat.</summary>
        Stack,
        /// <summary>
        /// Of the <see cref="Strongest"/> modifiers of a stat with the same <see cref="LockstepStatModifierType"/> and the
        /// same source <see cref="LockstepStatSource.kind"/>, only the one with the largest absolute value applies (the
        /// first of them on a tie): two leaders with the same aura give its bonus once, and the better one counts.
        /// </summary>
        Strongest,
    }
}
