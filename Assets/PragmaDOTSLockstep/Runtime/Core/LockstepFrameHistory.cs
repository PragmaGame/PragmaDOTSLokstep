using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Pragma.Lockstep
{
    /// <summary>Confirmed frames stored back to back, addressed by tick.</summary>
    /// <remarks>
    /// Holds ticks [<see cref="FirstTick"/>, <see cref="EndTick"/>). The server keeps the whole match (late join,
    /// replays); a client can drop frames it has already simulated with <see cref="DropBefore"/>.
    /// </remarks>
    public sealed unsafe class LockstepFrameHistory : IDisposable
    {
        private NativeList<byte> _data;
        // Start offset of every stored frame; one entry per tick.
        private NativeList<int> _offsets;
        private int _firstTick;

        public LockstepFrameHistory(int initialBytes = 4096)
        {
            _data = new NativeList<byte>(initialBytes, Allocator.Persistent);
            _offsets = new NativeList<int>(256, Allocator.Persistent);
        }

        public int FirstTick => _firstTick;
        public int Count => _offsets.Length;
        public int EndTick => _firstTick + _offsets.Length;
        public int ByteCount => _data.Length;

        public void Append(byte* frame, int length)
        {
            _offsets.Add(_data.Length);
            if (length > 0)
            {
                _data.AddRange(frame, length);
            }
        }

        public bool Contains(int tick) => tick >= _firstTick && tick < EndTick;

        public bool TryGet(int tick, out byte* frame, out int length)
        {
            if (!Contains(tick))
            {
                frame = null;
                length = 0;
                return false;
            }
            var index = tick - _firstTick;
            var start = _offsets[index];
            var end = index + 1 < _offsets.Length ? _offsets[index + 1] : _data.Length;
            frame = _data.GetUnsafePtr() + start;
            length = end - start;
            return true;
        }

        /// <summary>Forgets every frame before <paramref name="tick"/>.</summary>
        public void DropBefore(int tick)
        {
            var dropCount = Math.Min(tick - _firstTick, _offsets.Length);
            if (dropCount <= 0)
            {
                return;
            }

            var byteCount = dropCount < _offsets.Length ? _offsets[dropCount] : _data.Length;
            var remainingBytes = _data.Length - byteCount;
            if (remainingBytes > 0)
            {
                UnsafeUtility.MemMove(_data.GetUnsafePtr(), _data.GetUnsafePtr() + byteCount, remainingBytes);
            }
            _data.ResizeUninitialized(remainingBytes);

            var remainingOffsets = _offsets.Length - dropCount;
            for (var i = 0; i < remainingOffsets; i++)
            {
                _offsets[i] = _offsets[i + dropCount] - byteCount;
            }
            _offsets.ResizeUninitialized(remainingOffsets);
            _firstTick += dropCount;
        }

        public void Clear(int firstTick = 0)
        {
            _data.Clear();
            _offsets.Clear();
            _firstTick = firstTick;
        }

        public void Dispose()
        {
            if (_data.IsCreated)
            {
                _data.Dispose();
            }
            if (_offsets.IsCreated)
            {
                _offsets.Dispose();
            }
        }
    }
}
