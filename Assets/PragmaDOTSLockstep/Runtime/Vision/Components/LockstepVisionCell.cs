using Unity.Entities;

namespace Pragma.Lockstep.Vision
{
    /// <summary>
    /// One cell of one slot's plane of the <see cref="LockstepVisionGrid"/>, in a buffer on the grid entity: plane by plane
    /// in slot order, each row by row along X. Slot <c>s</c> sees cell <c>i</c> at index <c>s * CellCount + i</c>; read
    /// it through <see cref="LockstepVision"/>.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct LockstepVisionCell : IBufferElementData
    {
        /// <summary>Some vision source of the slot reaches into the cell on this tick.</summary>
        public bool isVisible;
    }
}
