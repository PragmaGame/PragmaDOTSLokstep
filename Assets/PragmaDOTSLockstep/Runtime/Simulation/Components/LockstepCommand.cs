using System;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// A discrete action (build, buy, use) with a game-defined payload struct. On the client the singleton buffer
    /// of <see cref="LockstepLocalInput"/> queues commands to send; in the simulation every player entity has a buffer
    /// with the commands confirmed for the current tick, cleared every tick.
    /// </summary>
    /// <remarks>Unlike per-tick input, a command is never dropped: when it arrives late it is moved to the next tick.</remarks>
    [InternalBufferCapacity(0)]
    public unsafe struct LockstepCommand : IBufferElementData
    {
        public const int MAX_PAYLOAD_SIZE = 122;

        /// <summary>Identifies the payload struct; see <see cref="TypeHashOf{T}"/>.</summary>
        public int typeHash;
        public byte size;
        private fixed byte _payload[MAX_PAYLOAD_SIZE];

        public static LockstepCommand Create<T>(in T payload) where T : unmanaged
        {
            var size = UnsafeUtility.SizeOf<T>();
            if (size > MAX_PAYLOAD_SIZE)
            {
                throw new ArgumentException($"A command payload is limited to {MAX_PAYLOAD_SIZE} bytes; {typeof(T)} has {size}.");
            }
            var command = new LockstepCommand { typeHash = TypeHashOf<T>(), size = (byte)size };
            var copy = payload;
            UnsafeUtility.MemCpy(command._payload, &copy, size);
            return command;
        }

        /// <summary>Builds a command from raw bytes; used when decoding network data.</summary>
        public static LockstepCommand FromRaw(int typeHash, byte* payload, int size)
        {
            var command = new LockstepCommand { typeHash = typeHash, size = (byte)Math.Min(size, MAX_PAYLOAD_SIZE) };
            UnsafeUtility.MemCpy(command._payload, payload, command.size);
            return command;
        }

        /// <summary>
        /// Stable 32-bit id of a payload type, a hash of its assembly-qualified name: identical in every build of the same
        /// code, but renaming the type, its namespace or its assembly changes it.
        /// </summary>
        public static int TypeHashOf<T>() where T : unmanaged => BurstRuntime.GetHashCode32<T>();

        public bool Is<T>() where T : unmanaged => typeHash == TypeHashOf<T>() && size == UnsafeUtility.SizeOf<T>();

        public bool TryGet<T>(out T payload) where T : unmanaged
        {
            if (!Is<T>())
            {
                payload = default;
                return false;
            }
            payload = Read<T>();
            return true;
        }

        public T Get<T>() where T : unmanaged
        {
            if (!Is<T>())
            {
                throw new InvalidOperationException("The command does not hold a payload of the requested type.");
            }
            return Read<T>();
        }

        internal void CopyPayloadTo(byte* destination)
        {
            fixed (byte* p = _payload)
            {
                UnsafeUtility.MemCpy(destination, p, size);
            }
        }

        private T Read<T>() where T : unmanaged
        {
            fixed (byte* p = _payload)
            {
                return UnsafeUtility.ReadArrayElement<T>(p, 0);
            }
        }
    }
}
