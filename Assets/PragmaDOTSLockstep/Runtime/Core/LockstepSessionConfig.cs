using System;
using Pragma.Lockstep.Mathematics;
using Unity.Collections;
using UnityEngine;

namespace Pragma.Lockstep
{
    /// <summary>
    /// Everything every client must agree on before tick 0. The server sends it in the start message and the
    /// simulation exposes it as <see cref="LockstepSessionInfo"/>.
    /// </summary>
    [Serializable]
    public struct LockstepSessionConfig : IEquatable<LockstepSessionConfig>
    {
        /// <summary>Simulation ticks per second.</summary>
        [field: SerializeField] public int TickRate { get; set; }
        [field: SerializeField] public int MaxPlayers { get; set; }
        /// <summary>Size in bytes of the input struct sent every tick.</summary>
        [field: SerializeField] public int InputSize { get; set; }
        /// <summary>Seed of <see cref="LockstepRandom"/> and of anything else that needs one.</summary>
        [field: SerializeField] public uint Seed { get; set; }
        /// <summary>Ticks between state checksums; zero disables desync detection.</summary>
        [field: SerializeField] public int ChecksumInterval { get; set; }
        /// <summary>Game-defined start parameters such as a map id or game mode.</summary>
        [field: SerializeField] public FixedList128Bytes<byte> StartData { get; set; }

        /// <summary>One tick in seconds, as <see cref="FixedPoint"/> (truncated to the fixed-point step).</summary>
        public FixedPoint DeltaTime => FixedPoint.FromFraction(1, TickRate);

        internal void Write(ref LockstepByteWriter writer)
        {
            writer.WriteUShort((ushort)TickRate);
            writer.WriteByte((byte)MaxPlayers);
            writer.WriteUShort((ushort)InputSize);
            writer.WriteUInt(Seed);
            writer.WriteUShort((ushort)ChecksumInterval);
            writer.WriteByte((byte)StartData.Length);
            for (var i = 0; i < StartData.Length; i++)
            {
                writer.WriteByte(StartData[i]);
            }
        }

        internal static LockstepSessionConfig Read(ref LockstepByteReader reader)
        {
            var config = new LockstepSessionConfig
            {
                TickRate = reader.ReadUShort(),
                MaxPlayers = reader.ReadByte(),
                InputSize = reader.ReadUShort(),
                Seed = reader.ReadUInt(),
                ChecksumInterval = reader.ReadUShort(),
            };
            var startDataLength = reader.ReadByte();
            if (startDataLength > LockstepProtocol.MAX_START_DATA_SIZE)
            {
                reader.Skip(int.MaxValue);
                return config;
            }
            // StartData is a property: fill a local list, appending to the property would only change a copy.
            var startData = new FixedList128Bytes<byte>();
            for (var i = 0; i < startDataLength; i++)
            {
                startData.Add(reader.ReadByte());
            }
            config.StartData = startData;
            return config;
        }

        /// <summary>Throws when a value is outside what the protocol supports.</summary>
        public void Validate()
        {
            if (TickRate < 1 || TickRate > 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(TickRate), TickRate, "Tick rate must be in [1, 1000].");
            }
            if (MaxPlayers < 1 || MaxPlayers > LockstepProtocol.MAX_PLAYERS)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxPlayers), MaxPlayers, $"Max players must be in [1, {LockstepProtocol.MAX_PLAYERS}].");
            }
            if (InputSize < 0 || InputSize > LockstepProtocol.MAX_INPUT_SIZE)
            {
                throw new ArgumentOutOfRangeException(nameof(InputSize), InputSize, $"Input size must be in [0, {LockstepProtocol.MAX_INPUT_SIZE}] bytes.");
            }
            if (ChecksumInterval < 0 || ChecksumInterval > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(ChecksumInterval), ChecksumInterval, "Checksum interval must be in [0, 65535].");
            }
        }

        public bool Equals(LockstepSessionConfig other)
        {
            return TickRate == other.TickRate && MaxPlayers == other.MaxPlayers && InputSize == other.InputSize &&
                   Seed == other.Seed && ChecksumInterval == other.ChecksumInterval && StartData.Equals(other.StartData);
        }

        public override bool Equals(object obj) => obj is LockstepSessionConfig other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = TickRate;
                hash = hash * 397 ^ MaxPlayers;
                hash = hash * 397 ^ InputSize;
                hash = hash * 397 ^ (int)Seed;
                hash = hash * 397 ^ ChecksumInterval;
                return hash;
            }
        }
    }
}
