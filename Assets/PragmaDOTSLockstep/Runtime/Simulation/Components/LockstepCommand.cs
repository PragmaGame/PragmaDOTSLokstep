using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// A discrete action (build, buy, use) with a game-defined payload struct and optional data of any length. On the
    /// client the singleton buffer of <see cref="LockstepLocalInput"/> queues commands to send; in the simulation every
    /// player entity has a buffer with the commands confirmed for the current tick, cleared every tick.
    /// </summary>
    /// <remarks>
    /// <para>Unlike per-tick input, a command is never dropped: when it arrives late it is moved to the next tick.</para>
    /// <para>The payload struct is stored inline, up to <see cref="MAX_PAYLOAD_SIZE"/> bytes. Lists (unit ids, waypoints)
    /// go into the command's data: bytes in the <see cref="LockstepCommandData"/> buffer of the same entity, with no size
    /// limit. Large commands are fragmented on the wire and still arrive in one tick.</para>
    /// </remarks>
    [InternalBufferCapacity(0)]
    public unsafe struct LockstepCommand : IBufferElementData
    {
        public const int MAX_PAYLOAD_SIZE = 122;

        // Data starts at multiples of this in LockstepCommandData, so it reads as any unmanaged element type.
        internal const int DATA_ALIGNMENT = 8;

        /// <summary>Identifies the payload struct; see <see cref="TypeHashOf{T}"/>.</summary>
        public int typeHash;
        public byte size;
        private fixed byte _payload[MAX_PAYLOAD_SIZE];
        private int _dataOffset;
        private int _dataLength;

        /// <summary>Size of the command's data in bytes; zero for a command without data.</summary>
        public int DataLength => _dataLength;

        /// <summary>Start of the data in the <see cref="LockstepCommandData"/> buffer the command was created with.</summary>
        internal int DataOffset => _dataOffset;

        public static LockstepCommand Create<T>(in T payload) where T : unmanaged
        {
            var size = UnsafeUtility.SizeOf<T>();
            if (size > MAX_PAYLOAD_SIZE)
            {
                throw new ArgumentException($"A command payload is limited to {MAX_PAYLOAD_SIZE} bytes; {typeof(T)} has {size}. Put lists into the command data.");
            }
            var command = new LockstepCommand { typeHash = TypeHashOf<T>(), size = (byte)size };
            var copy = payload;
            UnsafeUtility.MemCpy(command._payload, &copy, size);
            return command;
        }

        /// <summary>
        /// A command with data: <paramref name="data"/> is appended to <paramref name="dataBuffer"/>, which must be the
        /// <see cref="LockstepCommandData"/> buffer of the entity the command is added to.
        /// </summary>
        public static LockstepCommand Create<T, TData>(in T payload, NativeArray<TData> data, DynamicBuffer<LockstepCommandData> dataBuffer)
            where T : unmanaged
            where TData : unmanaged
        {
            var command = Create(payload);
            if (data.IsCreated && data.Length > 0)
            {
                command.AppendData(dataBuffer, (byte*)data.GetUnsafeReadOnlyPtr(), data.Length * UnsafeUtility.SizeOf<TData>());
            }
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

        /// <summary>
        /// The command's data as <typeparamref name="T"/> elements, read from <paramref name="dataBuffer"/>, the
        /// <see cref="LockstepCommandData"/> buffer of the same entity. The array is a view into the buffer, valid until
        /// the buffer changes. Trailing bytes that do not fill a whole element are ignored, the same way on every client.
        /// </summary>
        public NativeArray<T> GetData<T>(DynamicBuffer<LockstepCommandData> dataBuffer) where T : unmanaged
        {
            CheckDataRange(dataBuffer.Length);
            var elementSize = UnsafeUtility.SizeOf<T>();
            var bytes = dataBuffer.AsNativeArray().Reinterpret<byte>();
            return bytes.GetSubArray(_dataOffset, _dataLength / elementSize * elementSize).Reinterpret<T>(1);
        }

        /// <summary>Copies <paramref name="length"/> bytes to the end of <paramref name="dataBuffer"/> as the command's data.</summary>
        internal void AppendData(DynamicBuffer<LockstepCommandData> dataBuffer, byte* data, int length)
        {
            _dataOffset = 0;
            _dataLength = 0;
            if (length <= 0)
            {
                return;
            }

            var end = dataBuffer.Length;
            var offset = (end + DATA_ALIGNMENT - 1) & ~(DATA_ALIGNMENT - 1);
            dataBuffer.ResizeUninitialized(offset + length);
            var destination = (byte*)dataBuffer.GetUnsafePtr();
            // Padding is part of the hashed state: it must be the same bytes everywhere.
            UnsafeUtility.MemClear(destination + end, offset - end);
            UnsafeUtility.MemCpy(destination + offset, data, length);
            _dataOffset = offset;
            _dataLength = length;
        }

        internal void WritePayload(ref LockstepByteWriter writer)
        {
            fixed (byte* p = _payload)
            {
                writer.WriteBytes(p, size);
            }
        }

        private T Read<T>() where T : unmanaged
        {
            fixed (byte* p = _payload)
            {
                return UnsafeUtility.ReadArrayElement<T>(p, 0);
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private void CheckDataRange(int bufferLength)
        {
            if (_dataOffset < 0 || _dataLength < 0 || _dataOffset + _dataLength > bufferLength)
            {
                throw new InvalidOperationException("The command's data is outside the buffer; pass the LockstepCommandData buffer of the entity that holds the command.");
            }
        }
    }
}
