namespace Pragma.Lockstep.Stats
{
    /// <summary>
    /// How a <see cref="LockstepStatModifier"/> changes its attribute. The value of an attribute is
    /// <c>(base + sum of Flat) * max(0, 1 + sum of AdditivePercent) * product of max(0, 1 + MultiplicativePercent)</c>:
    /// the percentage factors stop at zero, the flat part may go below it.
    /// </summary>
    public enum LockstepStatModifierType : byte
    {
        /// <summary>Adds the value to the base: 20 adds 20.</summary>
        Flat,
        /// <summary>
        /// A share of the base and the flat modifiers, summed with the other additive ones: 0.25 and 0.15 add 40 %, a sum of -1
        /// or less makes the attribute zero.
        /// </summary>
        AdditivePercent,
        /// <summary>
        /// Scales the result by one plus the value, on top of the other multiplicative ones: 0.5 adds half, -0.5 halves it, -1
        /// or less makes the attribute zero.
        /// </summary>
        MultiplicativePercent,
    }
}
