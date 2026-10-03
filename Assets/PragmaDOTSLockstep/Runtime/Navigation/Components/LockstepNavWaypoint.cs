using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// The path of a <see cref="LockstepNavAgent"/>: the corners it walks through, the last one the end of the path. Written
    /// by <see cref="LockstepNavPathSystem"/>; Y is the height of the destination.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct LockstepNavWaypoint : IBufferElementData
    {
        public FixedVector3 position;
    }
}
