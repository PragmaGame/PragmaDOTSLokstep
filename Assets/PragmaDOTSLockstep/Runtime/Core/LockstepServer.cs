using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace Pragma.Lockstep
{
    /// <summary>
    /// Authority of a lockstep session. It never simulates: it orders inputs. Every tick it closes a frame with the
    /// input of every player, the commands, the joins and the leaves, and streams the frames to all clients.
    /// </summary>
    /// <remarks>
    /// <para>Ticks close on the server clock. A player whose input is not there in time gets the previous input
    /// repeated, so one slow connection does not stall the others; late commands move to the next tick instead of
    /// being dropped. Clients get timing feedback and shift their input clock until inputs arrive just in time.</para>
    /// <para>The class is transport-agnostic: feed it with <see cref="OnPacket"/> and connection events, call
    /// <see cref="Update"/> regularly, and it sends through the <see cref="ILockstepTransport"/>.</para>
    /// </remarks>
    public sealed unsafe class LockstepServer : ILockstepServerEndpoint, IDisposable
    {
        private sealed class Slot
        {
            public readonly int index;
            public readonly byte[] lastFrameInput;
            public readonly byte[] lastReceivedInput;
            public readonly byte[] ringInputs;
            public readonly int[] ringTicks;
            // Commands in their wire form: the server relays them without decoding.
            public readonly List<byte[]>[] ringCommands;
            public readonly List<byte[]> pendingCommands = new List<byte[]>();

            // Reserved by a player; stays reserved until the leave has been announced in a frame.
            public bool inUse;
            // Announced in a frame and not left yet.
            public bool active;
            public bool pendingJoin;
            public bool pendingLeave;
            public int connectionId = -1;
            public int joinTick;
            public bool reportsChecksums;
            public FixedList64Bytes<byte> joinData;
            public float minEarliness = float.MaxValue;
            public int lateDrops;
            public bool hasTiming;

            public Slot(int index, int inputSize, int ringSize)
            {
                this.index = index;
                lastFrameInput = new byte[inputSize];
                lastReceivedInput = new byte[inputSize];
                ringInputs = new byte[inputSize * ringSize];
                ringTicks = new int[ringSize];
                ringCommands = new List<byte[]>[ringSize];
                for (var i = 0; i < ringSize; i++)
                {
                    ringCommands[i] = new List<byte[]>();
                }
                Reset();
            }

            public void Reset()
            {
                inUse = false;
                active = false;
                pendingJoin = false;
                pendingLeave = false;
                connectionId = -1;
                joinTick = 0;
                reportsChecksums = false;
                joinData = default;
                Array.Clear(lastFrameInput, 0, lastFrameInput.Length);
                Array.Clear(lastReceivedInput, 0, lastReceivedInput.Length);
                for (var i = 0; i < ringTicks.Length; i++)
                {
                    ringTicks[i] = -1;
                    ringCommands[i].Clear();
                }
                pendingCommands.Clear();
                minEarliness = float.MaxValue;
                lateDrops = 0;
                hasTiming = false;
            }
        }

        private sealed class Connection
        {
            public int id;
            public int slot = -1;
            public bool started;
            public int nextFrameToSend;
        }

        private sealed class ChecksumRound
        {
            public int tick;
            public ulong reported;
            public ulong[] hashes;
        }

        private readonly LockstepServerSettings _settings;
        private readonly ILockstepTransport _transport;
        private readonly LockstepPacketFramer _framer;
        private readonly LockstepFrameHistory _history;
        private readonly Slot[] _slots;
        private readonly int _ringMask;
        private readonly Dictionary<int, Connection> _connections = new Dictionary<int, Connection>();
        private readonly List<Connection> _connectionOrder = new List<Connection>();
        private readonly List<byte[]> _receivedCommands = new List<byte[]>();
        private readonly Dictionary<int, ChecksumRound> _pendingChecksums = new Dictionary<int, ChecksumRound>();
        private readonly Dictionary<int, ulong> _resolvedChecksums = new Dictionary<int, ulong>();
        private readonly List<int> _checksumTicksToResolve = new List<int>();
        private NativeList<byte> _message;
        private NativeList<byte> _frame;
        private LockstepSessionConfig _config;
        private double _startTime;
        private int _ticksSinceFeedback;
        private bool _disposed;

        public LockstepServer(in LockstepServerSettings settings, ILockstepTransport transport)
        {
            settings.Validate();
            _settings = settings;
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _framer = new LockstepPacketFramer(settings.MaxPacketSize);
            _history = new LockstepFrameHistory();
            var ringSize = math.ceilpow2(settings.EffectiveMaxInputLeadTicks + 2);
            _ringMask = ringSize - 1;
            _slots = new Slot[settings.MaxPlayers];
            for (var i = 0; i < _slots.Length; i++)
            {
                _slots[i] = new Slot(i, settings.InputSize, ringSize);
            }
            _message = new NativeList<byte>(settings.MaxPacketSize * 2, Allocator.Persistent);
            _frame = new NativeList<byte>(256, Allocator.Persistent);
            _config = new LockstepSessionConfig
            {
                TickRate = settings.TickRate,
                MaxPlayers = settings.MaxPlayers,
                InputSize = settings.InputSize,
                Seed = settings.Seed,
                ChecksumInterval = settings.ChecksumInterval,
                StartData = settings.StartData,
            };
            State = LockstepServerState.Lobby;
        }

        public LockstepServerState State { get; private set; }
        public LockstepServerSettings Settings => _settings;
        /// <summary>The configuration sent to clients; the seed is final once the game started.</summary>
        public LockstepSessionConfig Config => _config;
        /// <summary>Number of closed ticks, which is also the next tick to close.</summary>
        public int ClosedTicks => _history.EndTick;
        /// <summary>All closed frames of the match.</summary>
        public LockstepFrameHistory History => _history;
        public double StartTime => _startTime;

        /// <summary>Players that joined and have not left.</summary>
        public int PlayerCount
        {
            get
            {
                var count = 0;
                foreach (var slot in _slots)
                {
                    if (slot.inUse && !slot.pendingLeave)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>Checksum rounds compared so far.</summary>
        public int VerifiedChecksumCount => _resolvedChecksums.Count;

        /// <summary>Inputs that arrived after their tick had closed and were replaced by the previous input.</summary>
        public int LateInputCount { get; private set; }

        /// <summary>Raised when clients disagree about the state at a tick.</summary>
        public event Action<LockstepDesyncReport> DesyncDetectedEvent;

        /// <summary>Raised with a connection id that sent malformed data; the transport may want to drop it.</summary>
        public event Action<int, string> ProtocolViolationEvent;

        /// <summary>The server clock in ticks: negative during the start countdown.</summary>
        public double GetTickPosition(double now) => State == LockstepServerState.Lobby ? 0 : (now - _startTime) * _settings.TickRate;

        public bool TryGetPlayerSlot(int connectionId, out int slot)
        {
            slot = _connections.TryGetValue(connectionId, out var connection) ? connection.slot : -1;
            return slot >= 0;
        }

        /// <summary>True when a player occupies the slot (joined, or about to be announced).</summary>
        public bool IsSlotInUse(int slot) => slot >= 0 && slot < _slots.Length && _slots[slot].inUse && !_slots[slot].pendingLeave;

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
            if (connection.slot >= 0)
            {
                ReleaseSlot(connection.slot);
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
            var type = (LockstepMessageType)reader.ReadByte();
            switch (type)
            {
                case LockstepMessageType.JoinRequest:
                    HandleJoin(connection, ref reader, now);
                    break;
                case LockstepMessageType.Input:
                    HandleInput(connection, ref reader, now);
                    break;
                case LockstepMessageType.Checksum:
                    HandleChecksum(connection, ref reader);
                    break;
                case LockstepMessageType.Ping:
                    HandlePing(connection, ref reader, now);
                    break;
                case LockstepMessageType.Leave:
                    if (connection.slot >= 0)
                    {
                        ReleaseSlot(connection.slot);
                    }
                    connection.slot = -1;
                    connection.started = false;
                    break;
                default:
                    ReportViolation(connection, $"unknown message type {(byte)type}");
                    break;
            }
        }

        /// <summary>Closes the ticks that are due and streams frames. Call it every server update.</summary>
        public void Update(double now)
        {
            if (_disposed || State != LockstepServerState.Running)
            {
                return;
            }

            CloseDueTicks(now);
            SendFrames();
            SendFeedback();
            ResolveChecksums();
        }

        /// <summary>Starts the match with everybody in the lobby; the first tick closes after the start delay.</summary>
        public void StartGame(double now)
        {
            if (State != LockstepServerState.Lobby)
            {
                return;
            }
            if (_config.Seed == 0)
            {
                _config.Seed = CreateSeed();
            }
            _startTime = now + Math.Max(0f, _settings.StartDelaySeconds);
            State = LockstepServerState.Running;
            foreach (var connection in _connectionOrder)
            {
                if (connection.slot < 0)
                {
                    continue;
                }
                connection.started = true;
                connection.nextFrameToSend = 0;
                SendStart(connection, now);
            }
        }

        /// <summary>Stops closing ticks and tells every client the match is over.</summary>
        public void EndGame(LockstepEndReason reason = LockstepEndReason.Finished)
        {
            if (State == LockstepServerState.Ended)
            {
                return;
            }
            var wasRunning = State == LockstepServerState.Running;
            State = LockstepServerState.Ended;
            if (!wasRunning)
            {
                return;
            }
            foreach (var connection in _connectionOrder)
            {
                if (!connection.started)
                {
                    continue;
                }
                // Flush what was closed so clients can finish the match.
                while (connection.nextFrameToSend < ClosedTicks)
                {
                    SendFramesMessage(connection);
                }
                var writer = BeginMessage(LockstepMessageType.End);
                writer.WriteByte((byte)reason);
                Send(connection.id);
            }
        }

        /// <summary>The whole match as a replay: configuration, frames and resolved checksums.</summary>
        public byte[] ExportReplay()
        {
            var checksums = new List<KeyValuePair<int, ulong>>(_resolvedChecksums);
            checksums.Sort((a, b) => a.Key.CompareTo(b.Key));
            return LockstepReplay.Write(_config, _history, checksums);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _framer.Dispose();
            _history.Dispose();
            if (_message.IsCreated)
            {
                _message.Dispose();
            }
            if (_frame.IsCreated)
            {
                _frame.Dispose();
            }
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

        private void HandleJoin(Connection connection, ref LockstepByteReader reader, double now)
        {
            var version = reader.ReadUShort();
            var simulationHash = reader.ReadULong();
            var flags = (LockstepJoinFlags)reader.ReadByte();
            var joinLength = reader.ReadByte();
            var joinBytes = reader.ReadBytesPtr(joinLength);

            if (reader.HasFailed || joinLength > LockstepProtocol.MAX_JOIN_DATA_SIZE)
            {
                Reject(connection, LockstepJoinRejectReason.InvalidRequest);
                return;
            }
            if (version != LockstepProtocol.VERSION)
            {
                Reject(connection, LockstepJoinRejectReason.ProtocolMismatch);
                return;
            }
            if (_settings.ValidateSimulationHash && simulationHash != LockstepSimulation.DefaultSimulationHash)
            {
                Reject(connection, LockstepJoinRejectReason.SimulationMismatch);
                return;
            }
            if (connection.slot >= 0)
            {
                return;
            }
            if (State == LockstepServerState.Ended)
            {
                Reject(connection, LockstepJoinRejectReason.GameEnded);
                return;
            }
            if (State == LockstepServerState.Running && !_settings.AllowLateJoin)
            {
                Reject(connection, LockstepJoinRejectReason.GameInProgress);
                return;
            }

            Slot slot = null;
            foreach (var candidate in _slots)
            {
                if (!candidate.inUse)
                {
                    slot = candidate;
                    break;
                }
            }
            if (slot == null)
            {
                Reject(connection, LockstepJoinRejectReason.SessionFull);
                return;
            }

            slot.Reset();
            slot.inUse = true;
            slot.pendingJoin = true;
            slot.connectionId = connection.id;
            slot.reportsChecksums = (flags & LockstepJoinFlags.Simulates) != 0;
            // The join record goes into the next closed frame.
            slot.joinTick = State == LockstepServerState.Running ? ClosedTicks : 0;
            slot.joinData.AddRange(joinBytes, joinLength);
            connection.slot = slot.index;

            var writer = BeginMessage(LockstepMessageType.JoinAccepted);
            writer.WriteByte((byte)slot.index);
            Send(connection.id);

            if (State == LockstepServerState.Running)
            {
                // Late join: the client re-simulates the match from tick 0 while frames stream in.
                connection.started = true;
                connection.nextFrameToSend = 0;
                SendStart(connection, now);
            }
            else if (_settings.MinPlayersToStart > 0 && PlayerCount >= _settings.MinPlayersToStart)
            {
                StartGame(now);
            }
        }

        private void HandleInput(Connection connection, ref LockstepByteReader reader, double now)
        {
            var firstTick = reader.ReadInt();
            var count = reader.ReadByte();
            if (connection.slot < 0)
            {
                return;
            }
            var slot = _slots[connection.slot];
            if (!slot.inUse || slot.pendingLeave)
            {
                return;
            }

            var inputSize = _settings.InputSize;
            for (var i = 0; i < count; i++)
            {
                var flags = (LockstepInputTickFlags)reader.ReadByte();
                if ((flags & LockstepInputTickFlags.Repeat) == 0 && inputSize > 0)
                {
                    fixed (byte* input = slot.lastReceivedInput)
                    {
                        reader.ReadBytes(input, inputSize);
                    }
                }

                _receivedCommands.Clear();
                if ((flags & LockstepInputTickFlags.Commands) != 0)
                {
                    var commandCount = reader.ReadByte();
                    for (var c = 0; c < commandCount && !reader.HasFailed; c++)
                    {
                        var start = reader.CurrentPtr;
                        var startPosition = reader.Position;
                        if (!LockstepCommandWire.TryRead(ref reader, out _, out _, out _, out _, out _))
                        {
                            break;
                        }
                        var command = new byte[reader.Position - startPosition];
                        fixed (byte* destination = command)
                        {
                            UnsafeUtility.MemCpy(destination, start, command.Length);
                        }
                        _receivedCommands.Add(command);
                    }
                }

                if (reader.HasFailed)
                {
                    ReportViolation(connection, "malformed input");
                    return;
                }
                StoreInput(slot, firstTick + i);
            }

            if (State == LockstepServerState.Running && count > 0)
            {
                // How many ticks before its deadline the most urgent input of the batch arrived.
                var earliness = (float)(firstTick + 1 - GetTickPosition(now));
                slot.minEarliness = Math.Min(slot.minEarliness, earliness);
                slot.hasTiming = true;
            }
        }

        private void StoreInput(Slot slot, int tick)
        {
            var closedTicks = ClosedTicks;
            if (State != LockstepServerState.Running || tick < closedTicks || tick < slot.joinTick ||
                tick >= closedTicks + _settings.EffectiveMaxInputLeadTicks)
            {
                if (State == LockstepServerState.Running && tick < closedTicks && tick >= slot.joinTick)
                {
                    slot.lateDrops++;
                    LateInputCount++;
                }
                // The input of that tick is lost, but commands are never dropped: they go to the next frame.
                slot.pendingCommands.AddRange(_receivedCommands);
                return;
            }

            var index = tick & _ringMask;
            slot.ringTicks[index] = tick;
            if (_settings.InputSize > 0)
            {
                Buffer.BlockCopy(slot.lastReceivedInput, 0, slot.ringInputs, index * _settings.InputSize, _settings.InputSize);
            }
            var commands = slot.ringCommands[index];
            commands.Clear();
            commands.AddRange(_receivedCommands);
        }

        private void HandleChecksum(Connection connection, ref LockstepByteReader reader)
        {
            var tick = reader.ReadInt();
            var hash = reader.ReadULong();
            if (reader.HasFailed || connection.slot < 0 || tick < 0 || tick >= ClosedTicks)
            {
                return;
            }
            var slot = _slots[connection.slot];
            if (!slot.reportsChecksums)
            {
                return;
            }

            var bit = 1UL << slot.index;
            if (_resolvedChecksums.TryGetValue(tick, out var resolved))
            {
                // Typically a late joiner re-simulating the past.
                if (hash != resolved)
                {
                    RaiseDesync(tick, bit);
                }
                return;
            }

            if (!_pendingChecksums.TryGetValue(tick, out var round))
            {
                round = new ChecksumRound { tick = tick, hashes = new ulong[_slots.Length] };
                _pendingChecksums.Add(tick, round);
            }
            round.hashes[slot.index] = hash;
            round.reported |= bit;
        }

        private void HandlePing(Connection connection, ref LockstepByteReader reader, double now)
        {
            var clientTime = reader.ReadDouble();
            if (reader.HasFailed)
            {
                return;
            }
            var writer = BeginMessage(LockstepMessageType.Pong);
            writer.WriteDouble(clientTime);
            writer.WriteDouble(State == LockstepServerState.Running ? GetTickPosition(now) : double.NaN);
            writer.WriteInt(ClosedTicks);
            Send(connection.id);
        }

        private void ReleaseSlot(int index)
        {
            var slot = _slots[index];
            if (!slot.inUse)
            {
                return;
            }
            if (slot.active)
            {
                // Announce the leave in the next frame; the slot stays reserved until then.
                slot.pendingLeave = true;
                slot.connectionId = -1;
            }
            else
            {
                // Never appeared in a frame: forget it silently.
                slot.Reset();
            }
        }

        private void CloseDueTicks(double now)
        {
            var position = GetTickPosition(now);
            while (true)
            {
                var tick = ClosedTicks;
                if (position < tick + 1)
                {
                    break;
                }
                if (_settings.MaxInputWaitTicks > 0 && position < tick + 1 + _settings.MaxInputWaitTicks && !AllInputsPresent(tick))
                {
                    break;
                }
                CloseTick(tick);
            }
        }

        private bool AllInputsPresent(int tick)
        {
            var index = tick & _ringMask;
            foreach (var slot in _slots)
            {
                if (!slot.inUse || slot.pendingLeave || slot.joinTick > tick)
                {
                    continue;
                }
                if (slot.ringTicks[index] != tick)
                {
                    return false;
                }
            }
            return true;
        }

        private void CloseTick(int tick)
        {
            _frame.Clear();
            var writer = new LockstepByteWriter(_frame);
            writer.WriteByte(0);
            var recordCount = 0;
            var inputSize = _settings.InputSize;
            var index = tick & _ringMask;

            foreach (var slot in _slots)
            {
                if (!slot.inUse)
                {
                    continue;
                }

                var flags = LockstepFrameRecordFlags.None;
                var joining = slot.pendingJoin;
                var leaving = slot.pendingLeave;
                if (joining)
                {
                    flags |= LockstepFrameRecordFlags.Joined;
                }
                if (leaving)
                {
                    flags |= LockstepFrameRecordFlags.Left;
                }

                var hasInput = slot.ringTicks[index] == tick;
                var commandCount = 0;
                if (!leaving)
                {
                    // A missing input simply repeats the previous one: no Input flag.
                    if (hasInput && inputSize > 0)
                    {
                        fixed (byte* ring = slot.ringInputs)
                        fixed (byte* last = slot.lastFrameInput)
                        {
                            var received = ring + index * inputSize;
                            if (UnsafeUtility.MemCmp(received, last, inputSize) != 0)
                            {
                                UnsafeUtility.MemCpy(last, received, inputSize);
                                flags |= LockstepFrameRecordFlags.Input;
                            }
                        }
                    }
                    if (hasInput)
                    {
                        slot.pendingCommands.AddRange(slot.ringCommands[index]);
                    }
                    commandCount = Math.Min(slot.pendingCommands.Count, LockstepProtocol.MAX_COMMANDS_PER_TICK);
                    if (commandCount > 0)
                    {
                        flags |= LockstepFrameRecordFlags.Commands;
                    }
                }
                if (hasInput)
                {
                    slot.ringTicks[index] = -1;
                    slot.ringCommands[index].Clear();
                }

                if (flags == LockstepFrameRecordFlags.None)
                {
                    continue;
                }

                writer.WriteByte((byte)slot.index);
                writer.WriteByte((byte)flags);
                if (joining)
                {
                    writer.WriteByte((byte)slot.joinData.Length);
                    for (var i = 0; i < slot.joinData.Length; i++)
                    {
                        writer.WriteByte(slot.joinData[i]);
                    }
                }
                if ((flags & LockstepFrameRecordFlags.Input) != 0)
                {
                    fixed (byte* last = slot.lastFrameInput)
                    {
                        writer.WriteBytes(last, inputSize);
                    }
                }
                if ((flags & LockstepFrameRecordFlags.Commands) != 0)
                {
                    writer.WriteByte((byte)commandCount);
                    for (var c = 0; c < commandCount; c++)
                    {
                        var command = slot.pendingCommands[c];
                        fixed (byte* bytes = command)
                        {
                            writer.WriteBytes(bytes, command.Length);
                        }
                    }
                    slot.pendingCommands.RemoveRange(0, commandCount);
                }
                recordCount++;

                if (joining)
                {
                    slot.pendingJoin = false;
                    slot.active = true;
                    slot.joinTick = tick;
                }
                if (leaving)
                {
                    slot.Reset();
                }
            }

            writer.SetByteAt(0, (byte)recordCount);
            _history.Append(_frame.GetUnsafePtr(), _frame.Length);
            _ticksSinceFeedback++;
        }

        private void SendFrames()
        {
            foreach (var connection in _connectionOrder)
            {
                if (!connection.started)
                {
                    continue;
                }
                var budget = _settings.MaxSendBytesPerUpdate;
                while (connection.nextFrameToSend < ClosedTicks && budget > 0)
                {
                    budget -= SendFramesMessage(connection);
                }
            }
        }

        // Packs as many consecutive frames as fit one packet (a single oversized frame is fragmented).
        private int SendFramesMessage(Connection connection)
        {
            var writer = BeginMessage(LockstepMessageType.Frames);
            writer.WriteInt(connection.nextFrameToSend);
            var countPosition = writer.Length;
            writer.WriteByte(0);

            var count = 0;
            var limit = _framer.MaxUnfragmentedMessageSize;
            while (connection.nextFrameToSend < ClosedTicks && count < byte.MaxValue)
            {
                _history.TryGet(connection.nextFrameToSend, out var frame, out var length);
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
            var size = writer.Length;
            Send(connection.id);
            return size;
        }

        private void SendFeedback()
        {
            if (_ticksSinceFeedback < Math.Max(1, _settings.FeedbackIntervalTicks))
            {
                return;
            }
            _ticksSinceFeedback = 0;
            foreach (var slot in _slots)
            {
                if (!slot.inUse || slot.connectionId < 0 || !slot.hasTiming)
                {
                    continue;
                }
                var writer = BeginMessage(LockstepMessageType.InputFeedback);
                writer.WriteShort((short)math.clamp((int)math.round(slot.minEarliness * 256f), short.MinValue, short.MaxValue));
                writer.WriteUShort((ushort)math.min(slot.lateDrops, ushort.MaxValue));
                Send(slot.connectionId);
                slot.hasTiming = false;
                slot.minEarliness = float.MaxValue;
                slot.lateDrops = 0;
            }
        }

        private void ResolveChecksums()
        {
            if (_pendingChecksums.Count == 0)
            {
                return;
            }
            var timeoutTicks = Math.Max(_settings.TickRate * 5, _settings.ChecksumInterval * 2);
            _checksumTicksToResolve.Clear();
            foreach (var pair in _pendingChecksums)
            {
                var round = pair.Value;
                var expected = ExpectedReporters(round.tick);
                var complete = expected != 0 && (round.reported & expected) == expected;
                if (complete || ClosedTicks - round.tick > timeoutTicks)
                {
                    _checksumTicksToResolve.Add(pair.Key);
                }
            }
            _checksumTicksToResolve.Sort();
            foreach (var tick in _checksumTicksToResolve)
            {
                ResolveChecksumRound(_pendingChecksums[tick]);
                _pendingChecksums.Remove(tick);
            }
        }

        private ulong ExpectedReporters(int tick)
        {
            var mask = 0UL;
            foreach (var slot in _slots)
            {
                if (slot.inUse && !slot.pendingLeave && slot.reportsChecksums && slot.joinTick <= tick && slot.connectionId >= 0)
                {
                    mask |= 1UL << slot.index;
                }
            }
            return mask;
        }

        private void ResolveChecksumRound(ChecksumRound round)
        {
            if (round.reported == 0)
            {
                return;
            }

            ulong majorityHash = 0;
            var majorityCount = 0;
            var tie = false;
            for (var i = 0; i < _slots.Length; i++)
            {
                if ((round.reported & (1UL << i)) == 0)
                {
                    continue;
                }
                var count = 0;
                for (var j = 0; j < _slots.Length; j++)
                {
                    if ((round.reported & (1UL << j)) != 0 && round.hashes[j] == round.hashes[i])
                    {
                        count++;
                    }
                }
                if (count > majorityCount)
                {
                    majorityCount = count;
                    majorityHash = round.hashes[i];
                    tie = false;
                }
                else if (count == majorityCount && round.hashes[i] != majorityHash)
                {
                    tie = true;
                }
            }

            var mismatch = 0UL;
            for (var i = 0; i < _slots.Length; i++)
            {
                var bit = 1UL << i;
                if ((round.reported & bit) != 0 && (tie || round.hashes[i] != majorityHash))
                {
                    mismatch |= bit;
                }
            }
            _resolvedChecksums[round.tick] = majorityHash;
            if (mismatch != 0)
            {
                RaiseDesync(round.tick, mismatch);
            }
        }

        private void RaiseDesync(int tick, ulong slotMask)
        {
            foreach (var connection in _connectionOrder)
            {
                if (!connection.started)
                {
                    continue;
                }
                var writer = BeginMessage(LockstepMessageType.Desync);
                writer.WriteInt(tick);
                writer.WriteULong(slotMask);
                Send(connection.id);
            }
            DesyncDetectedEvent?.Invoke(new LockstepDesyncReport { tick = tick, slotMask = slotMask });
        }

        private void SendStart(Connection connection, double now)
        {
            var slot = _slots[connection.slot];
            var writer = BeginMessage(LockstepMessageType.Start);
            writer.WriteUShort(LockstepProtocol.VERSION);
            _config.Write(ref writer);
            writer.WriteByte((byte)slot.index);
            writer.WriteInt(slot.joinTick);
            writer.WriteDouble(GetTickPosition(now));
            writer.WriteInt(ClosedTicks);
            Send(connection.id);
        }

        private void Reject(Connection connection, LockstepJoinRejectReason reason)
        {
            var writer = BeginMessage(LockstepMessageType.JoinRejected);
            writer.WriteByte((byte)reason);
            Send(connection.id);
        }

        private void ReportViolation(Connection connection, string reason)
        {
            Debug.LogWarning($"[Lockstep] Connection {connection.id} sent invalid data: {reason}.");
            ProtocolViolationEvent?.Invoke(connection.id, reason);
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

        private static uint CreateSeed()
        {
            var seed = (uint)Guid.NewGuid().GetHashCode();
            return seed == 0 ? 1u : seed;
        }
    }
}
