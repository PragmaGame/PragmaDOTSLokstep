using System;
using System.Collections.Generic;
using Unity.Collections;

namespace Pragma.Lockstep
{
    /// <summary>
    /// A recorded match: the session configuration, every confirmed frame and the checksums seen while it was played.
    /// Re-simulating the frames reproduces the match exactly; see <see cref="LockstepReplayPlayer"/>.
    /// </summary>
    public sealed unsafe class LockstepReplay : IDisposable
    {
        private const uint MAGIC = 0x50524C50; // "PLRP"
        private const ushort FORMAT_VERSION = 1;

        private readonly Dictionary<int, ulong> _checksums = new Dictionary<int, ulong>();

        private LockstepReplay(in LockstepSessionConfig config)
        {
            Config = config;
            Frames = new LockstepFrameHistory();
        }

        public LockstepSessionConfig Config { get; }
        public LockstepFrameHistory Frames { get; }
        public int FrameCount => Frames.Count;
        /// <summary>Recorded checksums by tick.</summary>
        public IReadOnlyDictionary<int, ulong> Checksums => _checksums;

        public static byte[] Write(in LockstepSessionConfig config, LockstepFrameHistory frames, IReadOnlyList<KeyValuePair<int, ulong>> checksums)
        {
            using (var buffer = new NativeList<byte>(frames.ByteCount + 256, Allocator.Temp))
            {
                var writer = new LockstepByteWriter(buffer);
                writer.WriteUInt(MAGIC);
                writer.WriteUShort(FORMAT_VERSION);
                writer.WriteUShort(LockstepProtocol.VERSION);
                config.Write(ref writer);
                writer.WriteInt(frames.FirstTick);
                writer.WriteInt(frames.Count);
                for (var tick = frames.FirstTick; tick < frames.EndTick; tick++)
                {
                    frames.TryGet(tick, out var frame, out var length);
                    writer.WriteVarUInt((uint)length);
                    writer.WriteBytes(frame, length);
                }
                var checksumCount = checksums?.Count ?? 0;
                writer.WriteInt(checksumCount);
                for (var i = 0; i < checksumCount; i++)
                {
                    writer.WriteInt(checksums[i].Key);
                    writer.WriteULong(checksums[i].Value);
                }
                return buffer.AsArray().ToArray();
            }
        }

        /// <exception cref="FormatException">The data is not a replay of a supported version.</exception>
        public static LockstepReplay Read(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            fixed (byte* bytes = data)
            {
                var reader = new LockstepByteReader(bytes, data.Length);
                if (reader.ReadUInt() != MAGIC)
                {
                    throw new FormatException("Not a lockstep replay.");
                }
                var formatVersion = reader.ReadUShort();
                var protocolVersion = reader.ReadUShort();
                if (formatVersion != FORMAT_VERSION || protocolVersion != LockstepProtocol.VERSION)
                {
                    throw new FormatException($"Replay format {formatVersion}/protocol {protocolVersion} is not supported.");
                }
                var config = LockstepSessionConfig.Read(ref reader);
                var firstTick = reader.ReadInt();
                var frameCount = reader.ReadInt();
                if (reader.HasFailed || firstTick != 0 || frameCount < 0)
                {
                    throw new FormatException("The replay header is corrupted.");
                }

                var replay = new LockstepReplay(config);
                try
                {
                    for (var i = 0; i < frameCount; i++)
                    {
                        var length = (int)reader.ReadVarUInt();
                        var frame = reader.ReadBytesPtr(length);
                        if (frame == null)
                        {
                            throw new FormatException($"The replay is truncated at frame {i}.");
                        }
                        replay.Frames.Append(frame, length);
                    }
                    var checksumCount = reader.ReadInt();
                    for (var i = 0; i < checksumCount && !reader.HasFailed; i++)
                    {
                        replay._checksums[reader.ReadInt()] = reader.ReadULong();
                    }
                    if (reader.HasFailed)
                    {
                        throw new FormatException("The replay checksums are truncated.");
                    }
                    config.Validate();
                    return replay;
                }
                catch
                {
                    replay.Dispose();
                    throw;
                }
            }
        }

        public void Dispose() => Frames.Dispose();
    }
}
