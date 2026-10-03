using System;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// The local player's input, a singleton in the client (or offline) world. Write it every frame from a system in
    /// <see cref="LockstepInputSystemGroup"/>; commands go into the <see cref="LockstepCommand"/> buffer on the same entity.
    /// </summary>
    /// <remarks>
    /// Values written here are quantized once, on this client, and then travel as bytes: converting a float stick
    /// value to <c>FixedPoint</c> here is fine, the simulation only ever sees the transmitted bytes.
    /// </remarks>
    public unsafe struct LockstepLocalInput : IComponentData
    {
        private fixed byte _data[LockstepProtocol.MAX_INPUT_SIZE];
        public int size;

        public void Set<T>(in T input) where T : unmanaged
        {
            var size = UnsafeUtility.SizeOf<T>();
            if (size > LockstepProtocol.MAX_INPUT_SIZE)
            {
                throw new ArgumentException($"{typeof(T)} is {size} bytes; input is limited to {LockstepProtocol.MAX_INPUT_SIZE}.");
            }
            var copy = input;
            fixed (byte* data = _data)
            {
                UnsafeUtility.MemClear(data, LockstepProtocol.MAX_INPUT_SIZE);
                UnsafeUtility.MemCpy(data, &copy, size);
            }
            this.size = size;
        }

        public T Get<T>() where T : unmanaged
        {
            fixed (byte* data = _data)
            {
                return UnsafeUtility.ReadArrayElement<T>(data, 0);
            }
        }

        internal void ApplyTo(LockstepClient client)
        {
            fixed (byte* data = _data)
            {
                client.SetInput(data, size);
            }
        }
    }
}
