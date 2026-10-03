using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Debug = UnityEngine.Debug;

namespace Pragma.Lockstep
{
    /// <summary>
    /// One player's side of a lockstep session: sends input, receives confirmed frames and steps the
    /// <see cref="LockstepSimulation"/> with them.
    /// </summary>
    /// <remarks>
    /// <para>Two clocks run here. The input clock decides which tick the current input belongs to; the server reports
    /// how early inputs arrive and the clock is shifted so they land about <see cref="LockstepClientSettings.InputMarginTicks"/>
    /// before their deadline. The playout clock decides when a confirmed frame is simulated; it keeps a small reserve of
    /// frames that grows with measured jitter, speeds up slightly when frames pile up and jumps when far behind
    /// (late join), with a per-update tick and time budget.</para>
    /// <para>The class is transport-agnostic: feed it with <see cref="OnPacket"/>, call <see cref="Update"/> every frame
    /// and it sends through the <see cref="ILockstepTransport"/>.</para>
    /// </remarks>
    public sealed unsafe class LockstepClient : IDisposable
    {
        private const int SERVER_CONNECTION_ID = 0;
        private const float FEEDBACK_IGNORE_SECONDS = 0.25f;

        private readonly LockstepClientSettings _settings;
        private readonly LockstepSimulationOptions _simulationOptions;
        private readonly ILockstepTransport _transport;
        private readonly LockstepPacketFramer _framer;
        private readonly LockstepFrameHistory _frames;
        // Commands in their wire form, queued until the next input message.
        private readonly List<byte[]> _pendingCommands = new List<byte[]>();
        private readonly List<KeyValuePair<int, ulong>> _localChecksums = new List<KeyValuePair<int, ulong>>();
        private readonly byte[] _input = new byte[LockstepProtocol.MAX_INPUT_SIZE];
        private readonly byte[] _lastSentInput = new byte[LockstepProtocol.MAX_INPUT_SIZE];
        private NativeList<byte> _message;
        private int _inputSize;
        private bool _hasSentInput;
        private bool _inputSizeWarningLogged;
        private int _lastSentInputTick;
        private double _lastUpdateTime = double.NaN;
        private double _lastPingTime = double.NegativeInfinity;
        private double _inputClock;
        private float _inputSpeed = 1f;
        private float _smoothedEarliness = float.NaN;
        private float _extraMargin;
        private double _ignoreFeedbackUntil;
        private double _playoutClock;
        private double _arrivalOffsetMean = double.NaN;
        private double _jitterSeconds;
        private bool _disposed;

        public LockstepClient(in LockstepClientSettings settings, ILockstepTransport transport, LockstepSimulationOptions simulationOptions = null)
        {
            _settings = settings;
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _simulationOptions = simulationOptions;
            _framer = new LockstepPacketFramer(settings.MaxPacketSize > 0 ? settings.MaxPacketSize : LockstepProtocol.DEFAULT_MAX_PACKET_SIZE);
            _frames = new LockstepFrameHistory();
            _message = new NativeList<byte>(256, Allocator.Persistent);
            LocalSlot = -1;
        }

        public LockstepClientSettings Settings => _settings;
        public LockstepClientState State { get; private set; }
        public LockstepJoinRejectReason RejectReason { get; private set; }
        /// <summary>Slot assigned by the server, or -1.</summary>
        public int LocalSlot { get; private set; }
        /// <summary>Tick on which the local player enters the simulation.</summary>
        public int JoinTick { get; private set; }
        /// <summary>Valid once <see cref="State"/> is <see cref="LockstepClientState.Running"/>.</summary>
        public LockstepSessionConfig Config { get; private set; }
        /// <summary>The simulation; null before the start and on clients that do not simulate.</summary>
        public LockstepSimulation Simulation { get; private set; }
        /// <summary>Confirmed frames received, which is also the first tick not yet received.</summary>
        public int ConfirmedTicks => _frames.EndTick;
        public int SimulatedTicks => Simulation?.Tick ?? 0;
        /// <summary>Latest tick an input was sent for.</summary>
        public int LastSentInputTick => _lastSentInputTick;
        /// <summary>Blend factor between the previous and the latest simulated tick for rendering, in [0, 1].</summary>
        public float InterpolationAlpha { get; private set; } = 1f;
        /// <summary>Smoothed round-trip time in seconds; NaN until the first pong.</summary>
        public double RoundTripTime { get; private set; } = double.NaN;
        /// <summary>Measured jitter of frame arrival, in ticks.</summary>
        public float JitterTicks => Config.TickRate > 0 ? (float)(_jitterSeconds * Config.TickRate) : 0f;
        /// <summary>Confirmed frames waiting to be simulated.</summary>
        public int BufferedTicks => ConfirmedTicks - SimulatedTicks;
        /// <summary>Checksums this client computed, by tick, in simulation order.</summary>
        public IReadOnlyList<KeyValuePair<int, ulong>> LocalChecksums => _localChecksums;
        public bool IsDesynced { get; private set; }
        /// <summary>First tick reported as desynced, or -1.</summary>
        public int DesyncTick { get; private set; } = -1;

        public event Action<LockstepClient> StartedEvent;
        /// <summary>Tick and whether the local client is one of the clients that disagree.</summary>
        public event Action<int, bool> DesyncedEvent;
        public event Action<LockstepClient> EndedEvent;
        public event Action<LockstepJoinRejectReason> RejectedEvent;

        /// <summary>Asks the server for a slot. <paramref name="joinData"/> reaches the simulation in <see cref="LockstepPlayer.joinData"/>.</summary>
        public void Join(in FixedList64Bytes<byte> joinData = default)
        {
            if (State != LockstepClientState.Idle && State != LockstepClientState.Rejected)
            {
                return;
            }
            var writer = BeginMessage(LockstepMessageType.JoinRequest);
            writer.WriteUShort(LockstepProtocol.VERSION);
            writer.WriteULong(LockstepSimulation.DefaultSimulationHash);
            writer.WriteByte((byte)(_settings.Simulate ? LockstepJoinFlags.Simulates : LockstepJoinFlags.None));
            writer.WriteByte((byte)joinData.Length);
            for (var i = 0; i < joinData.Length; i++)
            {
                writer.WriteByte(joinData[i]);
            }
            Send();
            State = LockstepClientState.Joining;
            RejectReason = LockstepJoinRejectReason.None;
        }

        /// <summary>Leaves the session; the server announces the leave to the other players.</summary>
        public void Leave()
        {
            if (State != LockstepClientState.Joining && State != LockstepClientState.Lobby && State != LockstepClientState.Running)
            {
                return;
            }
            BeginMessage(LockstepMessageType.Leave);
            Send();
            State = LockstepClientState.Ended;
        }

        /// <summary>The input for the ticks generated from now on, as raw bytes.</summary>
        public void SetInput(byte* data, int size)
        {
            if (size < 0 || size > LockstepProtocol.MAX_INPUT_SIZE)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, $"Input is limited to {LockstepProtocol.MAX_INPUT_SIZE} bytes.");
            }
            fixed (byte* input = _input)
            {
                UnsafeUtility.MemClear(input, LockstepProtocol.MAX_INPUT_SIZE);
                if (size > 0)
                {
                    UnsafeUtility.MemCpy(input, data, size);
                }
            }
            _inputSize = size;
        }

        public void SetInput<T>(in T input) where T : unmanaged
        {
            var copy = input;
            SetInput((byte*)&copy, UnsafeUtility.SizeOf<T>());
        }

        /// <summary>Queues a command; it is attached to the next tick an input is sent for.</summary>
        public void AddCommand<T>(in T payload) where T : unmanaged => AddCommand(LockstepCommand.Create(payload), null, 0);

        /// <summary>Queues a command with data of any length (see <see cref="LockstepCommandData"/>).</summary>
        public void AddCommand<T, TData>(in T payload, NativeArray<TData> data)
            where T : unmanaged
            where TData : unmanaged
        {
            var hasData = data.IsCreated && data.Length > 0;
            AddCommand(LockstepCommand.Create(payload),
                hasData ? (byte*)data.GetUnsafeReadOnlyPtr() : null,
                hasData ? data.Length * UnsafeUtility.SizeOf<TData>() : 0);
        }

        /// <summary>Queues a command whose data is <paramref name="dataLength"/> bytes at <paramref name="data"/>.</summary>
        internal void AddCommand(in LockstepCommand command, byte* data, int dataLength)
        {
            _pendingCommands.Add(LockstepCommandWire.Encode(command, data, dataLength));
        }

        public void OnPacket(byte* data, int length, double now)
        {
            if (_disposed || !_framer.Receive(SERVER_CONNECTION_ID, data, length, out var message, out var messageLength))
            {
                return;
            }

            var reader = new LockstepByteReader(message, messageLength);
            switch ((LockstepMessageType)reader.ReadByte())
            {
                case LockstepMessageType.JoinAccepted:
                    LocalSlot = reader.ReadByte();
                    if (State == LockstepClientState.Joining)
                    {
                        State = LockstepClientState.Lobby;
                    }
                    break;
                case LockstepMessageType.JoinRejected:
                    RejectReason = (LockstepJoinRejectReason)reader.ReadByte();
                    State = LockstepClientState.Rejected;
                    Debug.LogWarning($"[Lockstep] The server refused to join: {RejectReason}.");
                    RejectedEvent?.Invoke(RejectReason);
                    break;
                case LockstepMessageType.Start:
                    HandleStart(ref reader, now);
                    break;
                case LockstepMessageType.Frames:
                    HandleFrames(ref reader, now);
                    break;
                case LockstepMessageType.InputFeedback:
                    HandleFeedback(ref reader, now);
                    break;
                case LockstepMessageType.Pong:
                    HandlePong(ref reader, now);
                    break;
                case LockstepMessageType.Desync:
                    HandleDesync(ref reader);
                    break;
                case LockstepMessageType.End:
                    if (State == LockstepClientState.Running || State == LockstepClientState.Lobby)
                    {
                        State = LockstepClientState.Ended;
                        EndedEvent?.Invoke(this);
                    }
                    break;
            }
        }

        /// <param name="now">Monotonic time in seconds.</param>
        public void Update(double now)
        {
            if (_disposed)
            {
                return;
            }
            var deltaTime = double.IsNaN(_lastUpdateTime) ? 0 : Math.Max(0, now - _lastUpdateTime);
            _lastUpdateTime = now;

            if ((State == LockstepClientState.Joining || State == LockstepClientState.Lobby || State == LockstepClientState.Running) &&
                now - _lastPingTime >= _settings.PingIntervalSeconds)
            {
                SendPing(now);
            }

            if (State == LockstepClientState.Running)
            {
                _inputClock += deltaTime * Config.TickRate * _inputSpeed;
                SendInputs();
                if (Simulation == null)
                {
                    TryCreateSimulation();
                }
            }

            if (Simulation != null && (State == LockstepClientState.Running || State == LockstepClientState.Ended) &&
                !(_settings.StopOnDesync && IsDesynced))
            {
                AdvanceSimulation(deltaTime);
            }
        }

        /// <summary>The match so far as a replay; needs <see cref="LockstepClientSettings.RecordReplay"/>.</summary>
        public byte[] ExportReplay()
        {
            if (_frames.FirstTick != 0)
            {
                throw new InvalidOperationException("Frames were discarded; enable RecordReplay to export replays.");
            }
            return LockstepReplay.Write(Config, _frames, _localChecksums);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Simulation?.Dispose();
            Simulation = null;
            _framer.Dispose();
            _frames.Dispose();
            if (_message.IsCreated)
            {
                _message.Dispose();
            }
        }

        private float TargetMargin => _settings.InputMarginTicks + _extraMargin;

        private void TryCreateSimulation()
        {
            if (!_settings.Simulate || Simulation != null)
            {
                return;
            }
            var canCreate = _simulationOptions?.CanCreate;
            if (canCreate != null && !canCreate())
            {
                return;
            }
            Simulation = new LockstepSimulation(Config, _simulationOptions);
        }

        private void HandleStart(ref LockstepByteReader reader, double now)
        {
            var version = reader.ReadUShort();
            var config = LockstepSessionConfig.Read(ref reader);
            var slot = reader.ReadByte();
            var joinTick = reader.ReadInt();
            var position = reader.ReadDouble();
            reader.ReadInt();
            if (reader.HasFailed || version != LockstepProtocol.VERSION || State == LockstepClientState.Running)
            {
                return;
            }
            try
            {
                config.Validate();
            }
            catch (ArgumentException exception)
            {
                Debug.LogError($"[Lockstep] The server sent an invalid configuration: {exception.Message}");
                return;
            }

            Config = config;
            LocalSlot = slot;
            JoinTick = joinTick;
            TryCreateSimulation();

            // Input for tick T is due on the server when its clock reaches T + 1; aim for TargetMargin ticks earlier.
            // The start message is RTT/2 old, and the input needs another RTT/2 to travel.
            var rtt = double.IsNaN(RoundTripTime) ? 0 : RoundTripTime;
            _inputClock = Math.Max(joinTick, position + rtt * config.TickRate + TargetMargin - 1);
            _lastSentInputTick = joinTick - 1;
            _playoutClock = 0;
            _lastUpdateTime = now;
            State = LockstepClientState.Running;
            StartedEvent?.Invoke(this);
        }

        private void HandleFrames(ref LockstepByteReader reader, double now)
        {
            var firstTick = reader.ReadInt();
            var count = reader.ReadByte();
            if (State != LockstepClientState.Running && State != LockstepClientState.Ended)
            {
                return;
            }

            for (var i = 0; i < count; i++)
            {
                var length = (int)reader.ReadVarUInt();
                var frame = reader.ReadBytesPtr(length);
                if (frame == null)
                {
                    Debug.LogError("[Lockstep] Received a truncated frames message.");
                    return;
                }
                var tick = firstTick + i;
                if (tick < _frames.EndTick)
                {
                    continue;
                }
                if (tick > _frames.EndTick)
                {
                    Debug.LogError($"[Lockstep] Frame {tick} arrived but {_frames.EndTick} is missing; the channel must be reliable and ordered.");
                    return;
                }
                _frames.Append(frame, length);
            }

            // Live frames come one or two at a time; bursts (late join) would only distort the jitter estimate.
            if (count > 0 && count <= 2)
            {
                UpdateJitter(firstTick + count - 1, now);
            }
        }

        private void UpdateJitter(int tick, double now)
        {
            var offset = now - (double)tick / Config.TickRate;
            if (double.IsNaN(_arrivalOffsetMean))
            {
                _arrivalOffsetMean = offset;
                return;
            }
            var deviation = Math.Abs(offset - _arrivalOffsetMean);
            _arrivalOffsetMean += (offset - _arrivalOffsetMean) * 0.05;
            _jitterSeconds += (deviation - _jitterSeconds) * 0.1;
        }

        private void HandleFeedback(ref LockstepByteReader reader, double now)
        {
            var earliness = reader.ReadShort() / 256f;
            var lateDrops = reader.ReadUShort();
            if (reader.HasFailed || State != LockstepClientState.Running)
            {
                return;
            }

            // Inputs that came too late raise the safety margin; it slowly decays back while all is well.
            _extraMargin = lateDrops > 0 ? Math.Min(_extraMargin + 0.5f, 4f) : Math.Max(0f, _extraMargin - 0.02f);

            // Feedback describes inputs sent about one round trip ago; after a correction, wait for fresh data.
            if (now < _ignoreFeedbackUntil)
            {
                return;
            }

            _smoothedEarliness = float.IsNaN(_smoothedEarliness) ? earliness : _smoothedEarliness + (earliness - _smoothedEarliness) * 0.3f;
            var error = TargetMargin - _smoothedEarliness;
            if (Math.Abs(error) > 3f)
            {
                _inputClock += error;
                _inputSpeed = 1f;
                _smoothedEarliness = float.NaN;
                var rtt = double.IsNaN(RoundTripTime) ? 0.2 : RoundTripTime;
                _ignoreFeedbackUntil = now + rtt + FEEDBACK_IGNORE_SECONDS;
            }
            else
            {
                _inputSpeed = 1f + math.clamp(error * 0.05f, -0.05f, 0.1f);
            }
        }

        private void HandlePong(ref LockstepByteReader reader, double now)
        {
            var clientTime = reader.ReadDouble();
            if (reader.HasFailed)
            {
                return;
            }
            var sample = now - clientTime;
            if (sample < 0 || double.IsNaN(sample))
            {
                return;
            }
            RoundTripTime = double.IsNaN(RoundTripTime) ? sample : RoundTripTime + (sample - RoundTripTime) * 0.2;
        }

        private void HandleDesync(ref LockstepByteReader reader)
        {
            var tick = reader.ReadInt();
            var mask = reader.ReadULong();
            if (reader.HasFailed)
            {
                return;
            }
            var local = LocalSlot >= 0 && (mask & (1UL << LocalSlot)) != 0;
            if (!IsDesynced || tick < DesyncTick)
            {
                DesyncTick = tick;
            }
            IsDesynced = true;
            Debug.LogError($"[Lockstep] Desync detected at tick {tick} (slots mask 0x{mask:X}){(local ? ", including this client" : string.Empty)}.");
            DesyncedEvent?.Invoke(tick, local);
        }

        private void SendPing(double now)
        {
            _lastPingTime = now;
            var writer = BeginMessage(LockstepMessageType.Ping);
            writer.WriteDouble(now);
            Send();
        }

        private void SendInputs()
        {
            var target = (int)Math.Floor(_inputClock);
            if (target <= _lastSentInputTick)
            {
                return;
            }

            var first = _lastSentInputTick + 1;
            // After a long hitch, ticks far in the past are closed already: skip straight to recent ones.
            first = Math.Max(first, target - Config.TickRate * 2);

            var inputSize = Config.InputSize;
            if (_inputSize != inputSize && _inputSize != 0 && !_inputSizeWarningLogged)
            {
                _inputSizeWarningLogged = true;
                Debug.LogError($"[Lockstep] The input set on the client is {_inputSize} bytes but the session input size is {inputSize} bytes.");
            }

            fixed (byte* input = _input)
            fixed (byte* lastSent = _lastSentInput)
            {
                var inputChanged = !_hasSentInput || UnsafeUtility.MemCmp(input, lastSent, inputSize) != 0;
                while (first <= target)
                {
                    var count = Math.Min(byte.MaxValue, target - first + 1);
                    var writer = BeginMessage(LockstepMessageType.Input);
                    writer.WriteInt(first);
                    writer.WriteByte((byte)count);
                    for (var i = 0; i < count; i++)
                    {
                        var writeInput = i == 0 && inputChanged;
                        var commandCount = i == 0 ? Math.Min(_pendingCommands.Count, byte.MaxValue) : 0;
                        var flags = LockstepInputTickFlags.None;
                        if (!writeInput)
                        {
                            flags |= LockstepInputTickFlags.Repeat;
                        }
                        if (commandCount > 0)
                        {
                            flags |= LockstepInputTickFlags.Commands;
                        }
                        writer.WriteByte((byte)flags);
                        if (writeInput)
                        {
                            writer.WriteBytes(input, inputSize);
                        }
                        if (commandCount > 0)
                        {
                            writer.WriteByte((byte)commandCount);
                            for (var c = 0; c < commandCount; c++)
                            {
                                var command = _pendingCommands[c];
                                fixed (byte* bytes = command)
                                {
                                    writer.WriteBytes(bytes, command.Length);
                                }
                            }
                            _pendingCommands.RemoveRange(0, commandCount);
                        }
                    }
                    Send();

                    _lastSentInputTick = first + count - 1;
                    UnsafeUtility.MemCpy(lastSent, input, LockstepProtocol.MAX_INPUT_SIZE);
                    _hasSentInput = true;
                    inputChanged = false;
                    first += count;
                }
            }
        }

        private void AdvanceSimulation(double deltaTime)
        {
            var available = _frames.EndTick;
            var tickRate = Config.TickRate;
            var targetDelay = math.clamp(_settings.PlayoutDelayTicks + 2f * JitterTicks, 0f, Math.Max(_settings.PlayoutDelayTicks, _settings.MaxPlayoutDelayTicks));
            var desired = available + 1 - targetDelay;
            var error = desired - _playoutClock;
            var catchUpThreshold = Math.Max(4.0, tickRate * 0.25);
            if (error > catchUpThreshold)
            {
                _playoutClock = desired;
            }
            else
            {
                _playoutClock += deltaTime * tickRate * (1.0 + math.clamp(error * 0.1, -0.25, 0.25));
            }
            _playoutClock = Math.Min(_playoutClock, available + 1);

            var startTimestamp = Stopwatch.GetTimestamp();
            var budgetTicks = (long)(_settings.MaxSimulationMillisecondsPerUpdate * Stopwatch.Frequency / 1000.0);
            var simulatedThisUpdate = 0;
            while (Simulation.Tick < available && Simulation.Tick + 1 <= _playoutClock)
            {
                if (simulatedThisUpdate >= Math.Max(1, _settings.MaxTicksPerUpdate))
                {
                    break;
                }
                if (simulatedThisUpdate > 0 && Stopwatch.GetTimestamp() - startTimestamp >= budgetTicks)
                {
                    break;
                }
                StepSimulation();
                simulatedThisUpdate++;
            }

            // Out of budget: do not let the clock build up a debt, the next update jumps again if needed.
            if (Simulation.Tick + 1 < _playoutClock)
            {
                _playoutClock = Simulation.Tick + 1;
            }
            InterpolationAlpha = (float)math.clamp(_playoutClock - Simulation.Tick, 0.0, 1.0);

            if (!_settings.RecordReplay && Simulation.Tick - _frames.FirstTick > 1024)
            {
                _frames.DropBefore(Simulation.Tick);
            }
        }

        private void StepSimulation()
        {
            var tick = Simulation.Tick;
            _frames.TryGet(tick, out var frame, out var length);
            Simulation.Step(frame, length);

            if (Config.ChecksumInterval > 0 && tick % Config.ChecksumInterval == 0)
            {
                var hash = Simulation.ComputeChecksum();
                _localChecksums.Add(new KeyValuePair<int, ulong>(tick, hash));
                if (State == LockstepClientState.Running)
                {
                    var writer = BeginMessage(LockstepMessageType.Checksum);
                    writer.WriteInt(tick);
                    writer.WriteULong(hash);
                    Send();
                }
            }
        }

        private LockstepByteWriter BeginMessage(LockstepMessageType type)
        {
            _message.Clear();
            var writer = new LockstepByteWriter(_message);
            writer.WriteByte((byte)type);
            return writer;
        }

        private void Send()
        {
            _framer.Send(_transport, SERVER_CONNECTION_ID, _message.GetUnsafePtr(), _message.Length);
        }
    }
}
