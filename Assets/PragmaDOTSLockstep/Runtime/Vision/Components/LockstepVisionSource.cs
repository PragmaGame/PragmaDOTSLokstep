using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Vision
{
    /// <summary>
    /// An entity that lets a player slot see around itself (a unit, a building, a sensor): every cell of the
    /// <see cref="LockstepVisionGrid"/> that its circle of sight reaches into, around its <see cref="LockstepTransform"/>,
    /// is visible to <see cref="slot"/> on this tick.
    /// </summary>
    /// <remarks>
    /// Gameplay keeps both values current: the slot of the owner (a captured building changes hands) and the radius
    /// (research or a debuff changes sight). Allies that share vision are queried together; each source names one slot.
    /// </remarks>
    public struct LockstepVisionSource : IComponentData
    {
        /// <summary>How far it sees, in world units on the XZ plane, up to 30 000. Negative sees nothing.</summary>
        public FixedPoint radius;
        /// <summary>The player slot that sees through it. A slot outside the session's slots (-1 for neutral) sees nothing.</summary>
        public int slot;
    }
}
