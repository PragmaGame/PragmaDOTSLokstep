using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Pragma.Lockstep.Tests
{
    public unsafe class SerializationTests
    {
        private sealed class CapturingTransport : ILockstepTransport
        {
            public readonly List<(int Connection, byte[] Data)> packets = new List<(int, byte[])>();

            public void Send(int connectionId, byte* data, int length)
            {
                var bytes = new byte[length];
                fixed (byte* destination = bytes)
                {
                    UnsafeUtility.MemCpy(destination, data, length);
                }
                packets.Add((connectionId, bytes));
            }
        }

        [Test]
        public void WriterAndReader_RoundTripEveryType()
        {
            using (var buffer = new NativeList<byte>(Allocator.Temp))
            {
                var writer = new LockstepByteWriter(buffer);
                writer.WriteByte(250);
                writer.WriteBool(true);
                writer.WriteUShort(65000);
                writer.WriteShort(-1234);
                writer.WriteUInt(4000000000);
                writer.WriteInt(-2000000000);
                writer.WriteULong(ulong.MaxValue - 5);
                writer.WriteLong(long.MinValue + 7);
                writer.WriteDouble(-12.625);
                foreach (var value in new uint[] { 0, 1, 127, 128, 16383, 16384, uint.MaxValue })
                {
                    writer.WriteVarUInt(value);
                }
                foreach (var value in new[] { 0, -1, 1, -64, 64, int.MinValue, int.MaxValue })
                {
                    writer.WriteVarInt(value);
                }

                var reader = new LockstepByteReader(buffer.GetUnsafePtr(), buffer.Length);
                Assert.AreEqual(250, reader.ReadByte());
                Assert.IsTrue(reader.ReadBool());
                Assert.AreEqual(65000, reader.ReadUShort());
                Assert.AreEqual(-1234, reader.ReadShort());
                Assert.AreEqual(4000000000, reader.ReadUInt());
                Assert.AreEqual(-2000000000, reader.ReadInt());
                Assert.AreEqual(ulong.MaxValue - 5, reader.ReadULong());
                Assert.AreEqual(long.MinValue + 7, reader.ReadLong());
                Assert.AreEqual(-12.625, reader.ReadDouble());
                foreach (var value in new uint[] { 0, 1, 127, 128, 16383, 16384, uint.MaxValue })
                {
                    Assert.AreEqual(value, reader.ReadVarUInt());
                }
                foreach (var value in new[] { 0, -1, 1, -64, 64, int.MinValue, int.MaxValue })
                {
                    Assert.AreEqual(value, reader.ReadVarInt());
                }
                Assert.IsFalse(reader.HasFailed);
                Assert.IsTrue(reader.IsAtEnd);
            }
        }

        [Test]
        public void Reader_FailsPastTheEndWithoutThrowing()
        {
            var bytes = new byte[] { 1, 2, 3 };
            fixed (byte* data = bytes)
            {
                var reader = new LockstepByteReader(data, bytes.Length);
                Assert.AreEqual(0x0201, reader.ReadUShort());
                Assert.AreEqual(0, reader.ReadInt());
                Assert.IsTrue(reader.HasFailed);
                Assert.AreEqual(0, reader.ReadByte(), "reads after a failure return zero");

                var overlong = new LockstepByteReader(data, bytes.Length);
                overlong.Skip(-1);
                Assert.IsTrue(overlong.HasFailed);
            }

            var varint = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 };
            fixed (byte* data = varint)
            {
                var reader = new LockstepByteReader(data, varint.Length);
                reader.ReadVarUInt();
                Assert.IsTrue(reader.HasFailed, "a varint longer than 5 bytes is invalid");
            }
        }

        [Test]
        public void Framer_FragmentsAndReassemblesLargeMessages()
        {
            var transport = new CapturingTransport();
            using (var sender = new LockstepPacketFramer(64))
            using (var receiver = new LockstepPacketFramer(64))
            {
                var large = new byte[1000];
                for (var i = 0; i < large.Length; i++)
                {
                    large[i] = (byte)(i * 7);
                }
                var small = new byte[] { 9, 8, 7 };

                fixed (byte* data = large)
                {
                    sender.Send(transport, 3, data, large.Length);
                }
                fixed (byte* data = small)
                {
                    sender.Send(transport, 3, data, small.Length);
                }

                Assert.Greater(transport.packets.Count, 10);
                foreach (var packet in transport.packets)
                {
                    Assert.LessOrEqual(packet.Data.Length, 64);
                }

                var messages = new List<byte[]>();
                foreach (var packet in transport.packets)
                {
                    fixed (byte* data = packet.Data)
                    {
                        if (receiver.Receive(packet.Connection, data, packet.Data.Length, out var message, out var length))
                        {
                            var copy = new byte[length];
                            fixed (byte* destination = copy)
                            {
                                UnsafeUtility.MemCpy(destination, message, length);
                            }
                            messages.Add(copy);
                        }
                    }
                }

                Assert.AreEqual(2, messages.Count);
                CollectionAssert.AreEqual(large, messages[0]);
                CollectionAssert.AreEqual(small, messages[1]);
            }
        }

        [Test]
        public void FrameHistory_DropsOldFramesAndKeepsTicks()
        {
            using (var history = new LockstepFrameHistory())
            {
                for (var tick = 0; tick < 10; tick++)
                {
                    var frame = new byte[tick + 1];
                    for (var i = 0; i < frame.Length; i++)
                    {
                        frame[i] = (byte)tick;
                    }
                    fixed (byte* data = frame)
                    {
                        history.Append(data, frame.Length);
                    }
                }

                history.DropBefore(6);
                Assert.AreEqual(6, history.FirstTick);
                Assert.AreEqual(10, history.EndTick);
                Assert.IsFalse(history.TryGet(5, out _, out _));
                Assert.IsTrue(history.TryGet(8, out var frame8, out var length8));
                Assert.AreEqual(9, length8);
                Assert.AreEqual(8, frame8[0]);
                Assert.AreEqual(8, frame8[8]);
            }
        }

        [Test]
        public void SessionConfig_RoundTrips()
        {
            var config = TestUtility.Config(tickRate: 45, maxPlayers: 12, seed: 99, checksumInterval: 33);
            var startData = new FixedList128Bytes<byte>();
            startData.Add(1);
            startData.Add(2);
            config.StartData = startData;
            using (var buffer = new NativeList<byte>(Allocator.Temp))
            {
                var writer = new LockstepByteWriter(buffer);
                config.Write(ref writer);
                var reader = new LockstepByteReader(buffer.GetUnsafePtr(), buffer.Length);
                var read = LockstepSessionConfig.Read(ref reader);
                Assert.IsFalse(reader.HasFailed);
                Assert.AreEqual(config, read);
                Assert.AreEqual(2, read.StartData.Length);
                Assert.AreEqual(2, read.StartData[1]);
            }
        }

        [Test]
        public void Replay_RoundTrips()
        {
            var config = TestUtility.Config();
            using (var history = new LockstepFrameHistory())
            {
                var frames = new[] { new FrameBuilder().Join(0).Build(), new FrameBuilder().Input(0, new TestInput { moveX = 1 }).Build(), new byte[] { 0 } };
                foreach (var frame in frames)
                {
                    fixed (byte* data = frame)
                    {
                        history.Append(data, frame.Length);
                    }
                }
                var checksums = new List<KeyValuePair<int, ulong>> { new KeyValuePair<int, ulong>(0, 123), new KeyValuePair<int, ulong>(2, 456) };
                var bytes = LockstepReplay.Write(config, history, checksums);

                using (var replay = LockstepReplay.Read(bytes))
                {
                    Assert.AreEqual(config, replay.Config);
                    Assert.AreEqual(3, replay.FrameCount);
                    Assert.AreEqual(456UL, replay.Checksums[2]);
                    for (var tick = 0; tick < frames.Length; tick++)
                    {
                        replay.Frames.TryGet(tick, out var data, out var length);
                        Assert.AreEqual(frames[tick].Length, length);
                        for (var i = 0; i < length; i++)
                        {
                            Assert.AreEqual(frames[tick][i], data[i]);
                        }
                    }
                }

                bytes[0] ^= 0xFF;
                Assert.Throws<System.FormatException>(() => LockstepReplay.Read(bytes));
            }
        }
    }
}
