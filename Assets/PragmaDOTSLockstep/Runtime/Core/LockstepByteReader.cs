using Unity.Collections.LowLevel.Unsafe;

namespace Pragma.Lockstep
{
    /// <summary>Reads little-endian values written by <see cref="LockstepByteWriter"/>.</summary>
    /// <remarks>
    /// Reading past the end never throws: it sets <see cref="HasFailed"/> and returns zeros, so malformed network
    /// data can be checked once after parsing instead of after every field.
    /// </remarks>
    public unsafe struct LockstepByteReader
    {
        private readonly byte* _data;
        private readonly int _length;
        private int _position;
        private bool _failed;

        public LockstepByteReader(byte* data, int length)
        {
            _data = data;
            _length = length < 0 ? 0 : length;
            _position = 0;
            _failed = data == null && length > 0;
        }

        public int Position => _position;
        public int Length => _length;
        public int Remaining => _length - _position;
        public bool HasFailed => _failed;
        public bool IsAtEnd => _position >= _length;
        public byte* CurrentPtr => _data + _position;

        public byte ReadByte()
        {
            if (!Require(1))
            {
                return 0;
            }
            return _data[_position++];
        }

        public bool ReadBool() => ReadByte() != 0;

        public ushort ReadUShort()
        {
            if (!Require(2))
            {
                return 0;
            }
            var p = _data + _position;
            _position += 2;
            return (ushort)(p[0] | (p[1] << 8));
        }

        public short ReadShort() => (short)ReadUShort();

        public uint ReadUInt()
        {
            if (!Require(4))
            {
                return 0;
            }
            var p = _data + _position;
            _position += 4;
            return p[0] | ((uint)p[1] << 8) | ((uint)p[2] << 16) | ((uint)p[3] << 24);
        }

        public int ReadInt() => (int)ReadUInt();

        public ulong ReadULong()
        {
            if (!Require(8))
            {
                return 0;
            }
            var p = _data + _position;
            _position += 8;
            ulong value = 0;
            for (var i = 0; i < 8; i++)
            {
                value |= (ulong)p[i] << (i * 8);
            }
            return value;
        }

        public long ReadLong() => (long)ReadULong();

        public double ReadDouble()
        {
            var bits = ReadLong();
            return *(double*)&bits;
        }

        public uint ReadVarUInt()
        {
            uint value = 0;
            for (var shift = 0; shift < 35; shift += 7)
            {
                var b = ReadByte();
                if (_failed)
                {
                    return 0;
                }
                value |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return value;
                }
            }
            _failed = true;
            return 0;
        }

        public int ReadVarInt()
        {
            var encoded = ReadVarUInt();
            return (int)(encoded >> 1) ^ -(int)(encoded & 1);
        }

        public bool ReadBytes(byte* destination, int length)
        {
            if (length == 0)
            {
                return !_failed;
            }
            if (!Require(length))
            {
                return false;
            }
            UnsafeUtility.MemCpy(destination, _data + _position, length);
            _position += length;
            return true;
        }

        /// <summary>Returns a pointer to the next <paramref name="length"/> bytes and skips them; null on failure.</summary>
        public byte* ReadBytesPtr(int length)
        {
            if (!Require(length))
            {
                return null;
            }
            var p = _data + _position;
            _position += length;
            return p;
        }

        public void Skip(int length)
        {
            if (Require(length))
            {
                _position += length;
            }
        }

        private bool Require(int count)
        {
            if (_failed || count < 0 || count > _length - _position)
            {
                _failed = true;
                return false;
            }
            return true;
        }
    }
}
