using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Pragma.Lockstep
{
    /// <summary>Appends little-endian values to a <see cref="NativeList{T}"/> of bytes.</summary>
    /// <remarks>The writer only holds the list handle; copies of it write into the same list.</remarks>
    public unsafe struct LockstepByteWriter
    {
        private NativeList<byte> _buffer;

        public LockstepByteWriter(NativeList<byte> buffer)
        {
            _buffer = buffer;
        }

        public int Length => _buffer.Length;

        public NativeList<byte> Buffer => _buffer;

        public byte* GetUnsafePtr() => _buffer.GetUnsafePtr();

        public void Clear() => _buffer.Clear();

        public void WriteByte(byte value) => _buffer.Add(value);

        public void WriteBool(bool value) => _buffer.Add(value ? (byte)1 : (byte)0);

        public void WriteUShort(ushort value)
        {
            var p = Reserve(2);
            p[0] = (byte)value;
            p[1] = (byte)(value >> 8);
        }

        public void WriteShort(short value) => WriteUShort((ushort)value);

        public void WriteUInt(uint value)
        {
            var p = Reserve(4);
            p[0] = (byte)value;
            p[1] = (byte)(value >> 8);
            p[2] = (byte)(value >> 16);
            p[3] = (byte)(value >> 24);
        }

        public void WriteInt(int value) => WriteUInt((uint)value);

        public void WriteULong(ulong value)
        {
            var p = Reserve(8);
            for (var i = 0; i < 8; i++)
            {
                p[i] = (byte)(value >> (i * 8));
            }
        }

        public void WriteLong(long value) => WriteULong((ulong)value);

        /// <summary>Writes the IEEE bits. Only for timing data, never for simulation state.</summary>
        public void WriteDouble(double value) => WriteLong(*(long*)&value);

        /// <summary>LEB128: 1 byte below 128, up to 5 bytes.</summary>
        public void WriteVarUInt(uint value)
        {
            while (value >= 0x80)
            {
                _buffer.Add((byte)(value | 0x80));
                value >>= 7;
            }
            _buffer.Add((byte)value);
        }

        /// <summary>Zigzag + LEB128, so small negative values stay small.</summary>
        public void WriteVarInt(int value) => WriteVarUInt((uint)((value << 1) ^ (value >> 31)));

        public void WriteBytes(byte* data, int length)
        {
            if (length <= 0)
            {
                return;
            }
            UnsafeUtility.MemCpy(Reserve(length), data, length);
        }

        public void WriteBytes(NativeArray<byte> data) => WriteBytes((byte*)data.GetUnsafeReadOnlyPtr(), data.Length);

        /// <summary>Overwrites a byte written earlier, for counts that are only known at the end.</summary>
        public void SetByteAt(int position, byte value) => _buffer[position] = value;

        private byte* Reserve(int count)
        {
            var oldLength = _buffer.Length;
            _buffer.ResizeUninitialized(oldLength + count);
            return _buffer.GetUnsafePtr() + oldLength;
        }
    }
}
