using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// World Y of the ground at one corner of the <see cref="LockstepNavGrid"/> cells, in a buffer on the grid entity: row
    /// by row along X, <see cref="LockstepNavGrid.CornerCount"/> corners. Agents walk on the ground the corners describe
    /// (bilinear inside a cell, <see cref="LockstepNavigation.GetHeight"/>), and cells steeper than
    /// <see cref="LockstepNavGrid.maxSlope"/> are blocked. Without heights the ground is flat: agents keep their Y.
    /// </summary>
    /// <remarks>
    /// The heights are static: <see cref="LockstepNavObstacleSystem"/> reads them when it builds the cells, on the first
    /// navigation update. <c>LockstepNavGridAuthoring</c> bakes them from a terrain.
    /// </remarks>
    [InternalBufferCapacity(0)]
    public struct LockstepNavHeight : IBufferElementData
    {
        public FixedPoint value;
    }
}
