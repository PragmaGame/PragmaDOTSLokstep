using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Pragma.Lockstep.Tests
{
    /// <summary>Builds confirmed frames by hand, in the wire format the server produces.</summary>
    internal sealed unsafe class FrameBuilder
    {
        private sealed class Record
        {
            public LockstepFrameRecordFlags flags;
            public byte[] joinData = Array.Empty<byte>();
            public byte[] input = Array.Empty<byte>();
            // Wire form, as the server relays them.
            public readonly List<byte[]> commands = new List<byte[]>();
        }

        private readonly SortedDictionary<int, Record> _records = new SortedDictionary<int, Record>();

        public FrameBuilder Join(int slot, params byte[] joinData)
        {
            var record = Get(slot);
            record.flags |= LockstepFrameRecordFlags.Joined;
            record.joinData = joinData ?? Array.Empty<byte>();
            return this;
        }

        public FrameBuilder Leave(int slot)
        {
            Get(slot).flags |= LockstepFrameRecordFlags.Left;
            return this;
        }

        public FrameBuilder Input<T>(int slot, T input) where T : unmanaged
        {
            var record = Get(slot);
            record.flags |= LockstepFrameRecordFlags.Input;
            record.input = new byte[UnsafeUtility.SizeOf<T>()];
            fixed (byte* destination = record.input)
            {
                UnsafeUtility.CopyStructureToPtr(ref input, destination);
            }
            return this;
        }

        public FrameBuilder Command<T>(int slot, T payload) where T : unmanaged
        {
            var record = Get(slot);
            record.flags |= LockstepFrameRecordFlags.Commands;
            record.commands.Add(LockstepCommandWire.Encode(LockstepCommand.Create(payload), null, 0));
            return this;
        }

        public FrameBuilder Command<T, TData>(int slot, T payload, params TData[] data)
            where T : unmanaged
            where TData : unmanaged
        {
            var record = Get(slot);
            record.flags |= LockstepFrameRecordFlags.Commands;
            fixed (TData* elements = data)
            {
                record.commands.Add(LockstepCommandWire.Encode(LockstepCommand.Create(payload), (byte*)elements, data.Length * UnsafeUtility.SizeOf<TData>()));
            }
            return this;
        }

        public byte[] Build()
        {
            using (var buffer = new NativeList<byte>(64, Allocator.Temp))
            {
                var writer = new LockstepByteWriter(buffer);
                writer.WriteByte((byte)_records.Count);
                foreach (var pair in _records)
                {
                    var record = pair.Value;
                    writer.WriteByte((byte)pair.Key);
                    writer.WriteByte((byte)record.flags);
                    if ((record.flags & LockstepFrameRecordFlags.Joined) != 0)
                    {
                        writer.WriteByte((byte)record.joinData.Length);
                        foreach (var b in record.joinData)
                        {
                            writer.WriteByte(b);
                        }
                    }
                    if ((record.flags & LockstepFrameRecordFlags.Input) != 0)
                    {
                        foreach (var b in record.input)
                        {
                            writer.WriteByte(b);
                        }
                    }
                    if ((record.flags & LockstepFrameRecordFlags.Commands) != 0)
                    {
                        writer.WriteByte((byte)record.commands.Count);
                        foreach (var command in record.commands)
                        {
                            fixed (byte* bytes = command)
                            {
                                writer.WriteBytes(bytes, command.Length);
                            }
                        }
                    }
                }
                _records.Clear();
                return buffer.AsArray().ToArray();
            }
        }

        private Record Get(int slot)
        {
            if (!_records.TryGetValue(slot, out var record))
            {
                record = new Record();
                _records.Add(slot, record);
            }
            return record;
        }
    }
}
