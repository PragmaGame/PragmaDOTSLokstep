using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// The data of the commands in the <see cref="LockstepCommand"/> buffer of the same entity, back to back: lists of any
    /// length (unit ids, waypoints) that do not belong in a payload struct. Written with
    /// <see cref="LockstepCommand.Create{T, TData}"/>, read with <see cref="LockstepCommand.GetData{T}"/>; cleared together
    /// with the commands.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct LockstepCommandData : IBufferElementData
    {
        public byte value;
    }
}
