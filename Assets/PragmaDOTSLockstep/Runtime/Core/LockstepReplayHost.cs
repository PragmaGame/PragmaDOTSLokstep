using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Pragma.Lockstep
{
    /// <summary>
    /// Plays a recorded match to clients as if it were live: the server role of a <see cref="LockstepReplay"/>. A client
    /// joins as usual, gets the slot assigned to its connection with <see cref="Watch"/> and receives the recorded frames
    /// as the playback clock releases them, so a game presents a replay with the same code it presents a match with.
    /// </summary>
    /// <remarks>
    /// <para>The clock runs at <see cref="Speed"/> ticks per tick of real time and stops while <see cref="IsPaused"/>; set
    /// the same speed on the clients (<see cref="LockstepClient.PlaybackSpeed"/>) so their playout follows it smoothly.
    /// Once every frame is out the host ends the session, and the clients simulate what they still buffer.</para>
    /// <para>Input and commands of the clients are ignored. Their checksums are compared with the recorded ones: a state
    /// that differs - a replay of another build - is reported through <see cref="MismatchEvent"/> and to the client as a
    /// desync, once per connection: after the first difference every later state differs too.</para>
    /// </remarks>
    public sealed unsafe class LockstepReplayHost : ILockstepServerEndpoint, IDisposable
    {
        private sealed class Connection
        {
            public int id;
            public int slot = -1;
            public bool started;
            public bool ended;
            public bool hasMismatch;
            public int nextFrameToSend;
        }

        private readonly ILockstepTransport _transport;
        private readonly LockstepPacketFramer _framer;
        private readonly Dictionary<int, Connection> _connections = new Dictionary<int, Connection>();
        private readonly List<Connection> _connectionOrder = new List<Connection>();
        private readonly Dictionary<int, int> _watchedSlots = new Dictionary<int, int>();
        private NativeList<byte> _message;
        private double _clock;
        private double _lastUpdateTime = double.NaN;
        private float _speed = 1f;
        private bool _disposed;

        public LockstepReplayHost(LockstepReplay replay, ILockstepTransport transport, int maxPacketSize = LockstepProtocol.DEFAULT_MAX_PACKET_SIZE)
        {
            Replay = replay ?? throw new ArgumentNullException(nameof(replay));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _framer = new LockstepPacketFramer(maxPacketSize);
            _message = new NativeList<byte>(maxPacketSize * 2, Allocator.Persistent);
        }

        public LockstepReplay Replay { get; }

        public int FrameCount => Replay.FrameCount;

        /// <summary>The playback clock in ticks: the frames before it have been released to the clients.</summary>
        public double Position => _clock;

        /// <summary>Frames released to the clients so far.</summary>
        public int ReleasedTicks => (int)Math.Min(Math.Floor(_clock), FrameCount);

        /// <summary>Playback speed: 1 is real time, 2 twice as fast. Never negative.</summary>
        public float Speed
        {
            get => _speed;
            set => _speed = Math.Max(0f, value);
        }

        public bool IsPaused { get; set; }

        /// <summary>Every recorded frame has been released.</summary>
        public bool IsFinished => ReleasedTicks >= FrameCount;

        /// <summary>First tick a client reported a state that differs from the recording, or -1.</summary>
        public int FirstMismatchTick { get; private set; } = -1;

        /// <summary>Raised with the first tick a client's state differed from the recording, once per connection.</summary>
        public event Action<int> MismatchEvent;

        /// <summary>The slot whose view the connection gets: the client joins as that player. Assign it before the join.</summary>
        public void Watch(int connectionId, int slot)
        {
            if (slot < 0 || slot >= Replay.Config.MaxPlayers)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), slot, $"The replay has {Replay.Config.MaxPlayers} slots.");
            }
            _watchedSlots[connectionId] = slot;
        }

        public void OnConnected(int connectionId)
        {
            GetOrAddConnection(connectionId);
        }

        public void OnDisconnected(int connectionId)
        {
            if (!_connections.TryGetValue(connectionId, out var connection))
            {
                return;
            }
            _connections.Remove(connectionId);
            _connectionOrder.Remove(connection);
            _framer.RemoveConnection(connectionId);
        }

        public void OnPacket(int connectionId, byte* data, int length, double now)
        {
            if (_disposed)
            {
                return;
            }
            var connection = GetOrAddConnection(connectionId);
            if (!_framer.Receive(connectionId, data, length, out var message, out var messageLength))
            {
                return;
            }

            var reader = new LockstepByteReader(message, messageLength);
            switch ((LockstepMessageType)reader.ReadByte())
            {
                case LockstepMessageType.JoinRequest:
                    HandleJoin(connection, ref reader);
                    break;
                case LockstepMessageType.Checksum:
                    HandleChecksum(connection, ref reader);
                    break;
                case LockstepMessageType.Ping:
                    HandlePing(connection, ref reader);
                    break;
                case LockstepMessageType.Leave:
                    connection.started = false;
                    break;
            }
        }

        /// <summary>Advances the playback clock and streams the released frames. Call it every update.</summary>
        public void Update(double now)
        {
            if (_disposed)
            {
                return;
            }
            var deltaTime = double.IsNaN(_lastUpdateTime) ? 0 : Math.Max(0, now - _lastUpdateTime);
            _lastUpdateTime = now;
            if (!IsPaused)
            {
                _clock = Math.Min(_clock + deltaTime * Replay.Config.TickRate * _speed, FrameCount);
            }

            var released = ReleasedTicks;
            foreach (var connection in _connectionOrder)
            {
                if (!connection.started)
                {
                    continue;
                }
                while (connection.nextFrameToSend < released)
                {
                    SendFramesMessage(connection, released);
                }
                if (released >= FrameCount && !connection.ended)
                {
                    connection.ended = true;
                    var writer = BeginMessage(LockstepMessageType.End);
                    writer.WriteByte((byte)LockstepEndReason.Finished);
                    Send(connection.id);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _framer.Dispose();
            if (_message.IsCreated)
            {
                _message.Dispose();
            }
        }

        private void HandleJoin(Connection connection, ref LockstepByteReader reader)
        {
            var version = reader.ReadUShort();
            if (reader.HasFailed || version != LockstepProtocol.VERSION)
            {
                Reject(connection, LockstepJoinRejectReason.ProtocolMismatch);
                return;
            }
            if (connection.started)
            {
                return;
            }
            if (!_watchedSlots.TryGetValue(connection.id, out var slot))
            {
                Debug.LogWarning($"[Lockstep] Replay connection {connection.id} joined without a slot to watch; see LockstepReplayHost.Watch.");
                Reject(connection, LockstepJoinRejectReason.InvalidRequest);
                return;
            }

            connection.slot = slot;
            connection.started = true;
            connection.ended = false;
            connection.nextFrameToSend = 0;

            var accepted = BeginMessage(LockstepMessageType.JoinAccepted);
            accepted.WriteByte((byte)slot);
            Send(connection.id);

            var start = BeginMessage(LockstepMessageType.Start);
            start.WriteUShort(LockstepProtocol.VERSION);
            var config = Replay.Config;
            config.Write(ref start);
            start.WriteByte((byte)slot);
            start.WriteInt(GetJoinTick(slot));
            start.WriteDouble(_clock);
            start.WriteInt(ReleasedTicks);
            Send(connection.id);
        }

        private void HandleChecksum(Connection connection, ref LockstepByteReader reader)
        {
            var tick = reader.ReadInt();
            var hash = reader.ReadULong();
            if (reader.HasFailed || connection.hasMismatch || !Replay.Checksums.TryGetValue(tick, out var recorded) || recorded == hash)
            {
                return;
            }
            connection.hasMismatch = true;
            if (FirstMismatchTick < 0 || tick < FirstMismatchTick)
            {
                FirstMismatchTick = tick;
            }
            var writer = BeginMessage(LockstepMessageType.Desync);
            writer.WriteInt(tick);
            writer.WriteULong(connection.slot >= 0 ? 1UL << connection.slot : 0UL);
            Send(connection.id);
            MismatchEvent?.Invoke(tick);
        }

        private void HandlePing(Connection connection, ref LockstepByteReader reader)
        {
            var clientTime = reader.ReadDouble();
            if (reader.HasFailed)
            {
                return;
            }
            var writer = BeginMessage(LockstepMessageType.Pong);
            writer.WriteDouble(clientTime);
            writer.WriteDouble(_clock);
            writer.WriteInt(ReleasedTicks);
            Send(connection.id);
        }

        /// <summary>The tick the watched player entered on; a slot nobody played enters at the start.</summary>
        private int GetJoinTick(int slot)
        {
            foreach (var player in Replay.Players)
            {
                if (player.slot == slot)
                {
                    return player.joinTick;
                }
            }
            return 0;
        }

        // Packs as many consecutive frames as fit one packet (a single oversized frame is fragmented).
        private void SendFramesMessage(Connection connection, int released)
        {
            var writer = BeginMessage(LockstepMessageType.Frames);
            writer.WriteInt(connection.nextFrameToSend);
            var countPosition = writer.Length;
            writer.WriteByte(0);

            var count = 0;
            var limit = _framer.MaxUnfragmentedMessageSize;
            while (connection.nextFrameToSend < released && count < byte.MaxValue)
            {
                Replay.Frames.TryGet(connection.nextFrameToSend, out var frame, out var length);
                if (count > 0 && writer.Length + 5 + length > limit)
                {
                    break;
                }
                writer.WriteVarUInt((uint)length);
                writer.WriteBytes(frame, length);
                connection.nextFrameToSend++;
                count++;
            }

            writer.SetByteAt(countPosition, (byte)count);
            Send(connection.id);
        }

        private void Reject(Connection connection, LockstepJoinRejectReason reason)
        {
            var writer = BeginMessage(LockstepMessageType.JoinRejected);
            writer.WriteByte((byte)reason);
            Send(connection.id);
        }

        private Connection GetOrAddConnection(int connectionId)
        {
            if (!_connections.TryGetValue(connectionId, out var connection))
            {
                connection = new Connection { id = connectionId };
                _connections.Add(connectionId, connection);
                _connectionOrder.Add(connection);
            }
            return connection;
        }

        private LockstepByteWriter BeginMessage(LockstepMessageType type)
        {
            _message.Clear();
            var writer = new LockstepByteWriter(_message);
            writer.WriteByte((byte)type);
            return writer;
        }

        private void Send(int connectionId)
        {
            _framer.Send(_transport, connectionId, _message.GetUnsafePtr(), _message.Length);
        }
    }
}
