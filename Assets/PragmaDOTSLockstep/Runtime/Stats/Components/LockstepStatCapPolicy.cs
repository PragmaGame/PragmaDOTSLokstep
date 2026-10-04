namespace Pragma.Lockstep.Stats
{
    /// <summary>What the amount of a capped resource does when its cap changes.</summary>
    public enum LockstepStatCapPolicy : byte
    {
        /// <summary>
        /// Keeps its share of the cap: 80 of 100 becomes 120 of 150, and back to 80 of 100. A bonus to the maximum neither
        /// heals nor wounds, and a full resource stays full.
        /// </summary>
        KeepRatio,
        /// <summary>Keeps the amount and is cut down to a lower cap: 80 of 100 becomes 80 of 150, and 50 of 50.</summary>
        Clamp,
    }
}
