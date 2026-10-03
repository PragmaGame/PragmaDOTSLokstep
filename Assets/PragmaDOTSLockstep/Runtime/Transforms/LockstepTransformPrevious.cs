using System.Runtime.CompilerServices;
using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// <see cref="LockstepTransform"/> as it was at the start of the latest tick, for interpolating the presentation.
    /// Written by <see cref="LockstepTransformHistorySystem"/>; add it to entities whose views move smoothly.
    /// </summary>
    public struct LockstepTransformPrevious : IComponentData
    {
        public FixedVector3 position;
        public FixedQuaternion rotation;
        public FixedPoint scale;
        /// <summary>Tick + 1 of the capture, so zero (the default of new entities) is never mistaken for tick 0.</summary>
        public int capturedTickPlusOne;

        /// <summary>True when the value was captured at the start of <paramref name="tick"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsCapturedAt(int tick) => capturedTickPlusOne == tick + 1;
    }
}
