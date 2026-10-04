using System;

namespace Pragma.Lockstep.Stats
{
    /// <summary>
    /// What applied a <see cref="LockstepStatModifier"/>, a <see cref="LockstepStatGrant"/> or a <see cref="LockstepStatChange"/>.
    /// The modifiers of one source are removed together (<see cref="LockstepStats.RemoveModifiers"/>), so an effect that
    /// ends, or is applied again, takes all of its modifiers along.
    /// </summary>
    /// <remarks>
    /// The game picks both numbers: <see cref="kind"/> for the kind of effect (an ability, a research, an aura, cover) and
    /// <see cref="id"/> for which one or whose (the ability's index, the caster's <see cref="LockstepEntityId"/>). Applying
    /// an effect again after removing its source replaces it; <see cref="LockstepStatStacking.Strongest"/> modifiers of one
    /// kind from different sources do not add up either.
    /// </remarks>
    public struct LockstepStatSource : IEquatable<LockstepStatSource>
    {
        public uint kind;
        public uint id;

        public LockstepStatSource(uint kind, uint id)
        {
            this.kind = kind;
            this.id = id;
        }

        public static bool operator ==(LockstepStatSource a, LockstepStatSource b) => a.kind == b.kind && a.id == b.id;

        public static bool operator !=(LockstepStatSource a, LockstepStatSource b) => !(a == b);

        public bool Equals(LockstepStatSource other) => this == other;

        public override bool Equals(object obj) => obj is LockstepStatSource other && this == other;

        public override int GetHashCode() => unchecked((int)kind * 397 ^ (int)id);

        public override string ToString() => $"LockstepStatSource({kind}, {id})";
    }
}
