namespace Pragma.Lockstep.Stats
{
    /// <summary>
    /// How a <see cref="LockstepStatModifier"/> changes its stat. The value of a stat is
    /// <c>(base + sum of Flat) * (1 + sum of Additive) * product of (1 + Multiplicative)</c>.
    /// </summary>
    public enum LockstepStatModifierType : byte
    {
        /// <summary>Adds the value to the base: 20 adds 20.</summary>
        Flat,
        /// <summary>A share of the base and the flat modifiers, summed with the other additive ones: 0.25 and 0.15 add 40 %.</summary>
        Additive,
        /// <summary>Scales the result by one plus the value, on top of the other multiplicative ones: 0.5 adds half, -0.5 halves it.</summary>
        Multiplicative,
    }
}
