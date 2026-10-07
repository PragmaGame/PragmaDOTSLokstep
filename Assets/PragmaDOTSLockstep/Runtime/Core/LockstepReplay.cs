using System;
using System.Collections.Generic;
using Unity.Collections;

namespace Pragma.Lockstep
{
    /// <summary>
    /// A recorded match: the session configuration, every confirmed frame and the checksums seen while it was played.
    /// Re-simulating the frames reproduces the match exactly; see <see cref="LockstepReplayPlayer"/>, or
    /// <see cref="LockstepReplayHost"/> to watch it through clients.
    /// </summary>
    public sealed unsafe class LockstepReplay : IDisposable
    {
        private const uint MAGIC = 0x50524C50; // "PLRP"
        private const ushort FORMAT_VERSION = 1;

        private readonly Dictionary<int, ulong> _checksums = new Dictionary<int, ulong>();
        private readonly List<LockstepReplaySlot> _players = new List<LockstepReplaySlot>();

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
        /// <summary>The players of the match in the order they entered; a slot freed by a leave can appear again.</summary>
        public IReadOnlyList<LockstepReplaySlot> Players => _players;
        /// <summary>Length of the match in seconds.</summary>
        public double DurationSeconds => (double)FrameCount / Config.TickRate;

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
                    replay.ReadPlayers();
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

        /// <summary>Finds the joins and leaves in the frames, reading each record the way the simulation applies it.</summary>
        private void ReadPlayers()
        {
            var open = new Dictionary<int, int>();
            for (var tick = Frames.FirstTick; tick < Frames.EndTick; tick++)
            {
                Frames.TryGet(tick, out var frame, out var length);
                var reader = new LockstepByteReader(frame, length);
                var recordCount = length == 0 ? 0 : reader.ReadByte();
                for (var r = 0; r < recordCount && !reader.HasFailed; r++)
                {
                    var slot = reader.ReadByte();
                    var flags = (LockstepFrameRecordFlags)reader.ReadByte();
                    if (slot >= Config.MaxPlayers)
                    {
                        throw new FormatException($"The replay frame {tick} names slot {slot} of {Config.MaxPlayers}.");
                    }
                    if ((flags & LockstepFrameRecordFlags.Joined) != 0)
                    {
                        var joinLength = reader.ReadByte();
                        var joinBytes = reader.ReadBytesPtr(joinLength);
                        if (joinBytes == null || joinLength > LockstepProtocol.MAX_JOIN_DATA_SIZE)
                        {
                            throw new FormatException($"The replay frame {tick} is malformed.");
                        }
                        var joinData = new FixedList64Bytes<byte>();
                        joinData.AddRange(joinBytes, joinLength);
                        open[slot] = _players.Count;
                        _players.Add(new LockstepReplaySlot(slot, tick, -1, joinData));
                    }
                    if ((flags & LockstepFrameRecordFlags.Left) != 0 && open.TryGetValue(slot, out var index))
                    {
                        var player = _players[index];
                        _players[index] = new LockstepReplaySlot(player.slot, player.joinTick, tick, player.joinData);
                        open.Remove(slot);
                    }
                    if ((flags & LockstepFrameRecordFlags.Input) != 0)
                    {
                        reader.Skip(Config.InputSize);
                    }
                    if ((flags & LockstepFrameRecordFlags.Commands) != 0)
                    {
                        var commandCount = reader.ReadByte();
                        for (var c = 0; c < commandCount; c++)
                        {
                            if (!LockstepCommandWire.TryRead(ref reader, out _, out _, out _, out _, out _))
                            {
                                break;
                            }
                        }
                    }
                }
                if (reader.HasFailed)
                {
                    throw new FormatException($"The replay frame {tick} is malformed.");
                }
            }
        }
    }
}
