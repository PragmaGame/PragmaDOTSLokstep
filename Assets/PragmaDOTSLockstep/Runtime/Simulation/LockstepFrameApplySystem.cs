using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// First system of every tick: turns the confirmed frame into simulation state.
    /// </summary>
    /// <remarks>
    /// Order inside a tick: players that left on the previous tick are destroyed, every remaining player gets its
    /// previous input saved and its commands cleared, then the frame records are applied (joins create player
    /// entities, input and commands are written, leaves enable <see cref="LockstepPlayerLeft"/>).
    /// </remarks>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup), OrderFirst = true)]
    [BurstCompile]
    public partial struct LockstepFrameApplySystem : ISystem
    {
        private EntityArchetype _playerArchetype;
        private EntityQuery _playersQuery;
        private EntityQuery _leftPlayersQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            // A span, not params: Burst cannot allocate the managed array.
            _playerArchetype = state.EntityManager.CreateArchetype(stackalloc ComponentType[]
            {
                ComponentType.ReadWrite<LockstepPlayer>(),
                ComponentType.ReadWrite<LockstepPlayerInput>(),
                ComponentType.ReadWrite<LockstepPlayerJoined>(),
                ComponentType.ReadWrite<LockstepPlayerLeft>(),
                ComponentType.ReadWrite<LockstepCommand>(),
                ComponentType.ReadWrite<LockstepCommandData>(),
            });
            _playersQuery = SystemAPI.QueryBuilder().WithAll<LockstepPlayer>().Build();
            _leftPlayersQuery = SystemAPI.QueryBuilder().WithAll<LockstepPlayer, LockstepPlayerLeft>().Build();
            state.RequireForUpdate<LockstepSessionInfo>();
            state.RequireForUpdate<LockstepTime>();
        }

        [BurstCompile]
        public unsafe void OnUpdate(ref SystemState state)
        {
            var entityManager = state.EntityManager;
            var time = SystemAPI.GetSingleton<LockstepTime>();
            var info = SystemAPI.GetSingleton<LockstepSessionInfo>();
            var sessionEntity = SystemAPI.GetSingletonEntity<LockstepSessionInfo>();

            // 1. Players whose leave was announced on the previous tick disappear now.
            if (!_leftPlayersQuery.IsEmpty)
            {
                var leaving = _leftPlayersQuery.ToEntityArray(Allocator.Temp);
                var slots = entityManager.GetBuffer<LockstepPlayerSlot>(sessionEntity);
                for (var i = 0; i < leaving.Length; i++)
                {
                    var slot = entityManager.GetComponentData<LockstepPlayer>(leaving[i]).slot;
                    if (slot >= 0 && slot < slots.Length && slots[slot].player == leaving[i])
                    {
                        slots[slot] = default;
                    }
                }
                entityManager.DestroyEntity(leaving);
            }

            // 2. Per-tick reset of the remaining players.
            var players = _playersQuery.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                var input = entityManager.GetComponentData<LockstepPlayerInput>(player);
                input.ShiftCurrentToPrevious();
                entityManager.SetComponentData(player, input);
                entityManager.GetBuffer<LockstepCommand>(player).Clear();
                entityManager.GetBuffer<LockstepCommandData>(player).Clear();
                entityManager.SetComponentEnabled<LockstepPlayerJoined>(player, false);
            }

            // 3. Apply the frame. Copy it first: creating players moves chunk memory.
            var frameBuffer = entityManager.GetBuffer<LockstepFrameData>(sessionEntity, true);
            var frame = new NativeArray<byte>(frameBuffer.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            if (frame.Length > 0)
            {
                UnsafeUtility.MemCpy(frame.GetUnsafePtr(), frameBuffer.GetUnsafeReadOnlyPtr(), frame.Length);
            }

            var reader = new LockstepByteReader((byte*)frame.GetUnsafeReadOnlyPtr(), frame.Length);
            var recordCount = frame.Length == 0 ? 0 : reader.ReadByte();
            for (var r = 0; r < recordCount && !reader.HasFailed; r++)
            {
                var slot = reader.ReadByte();
                var flags = (LockstepFrameRecordFlags)reader.ReadByte();
                if (slot >= info.maxPlayers)
                {
                    reader.Skip(int.MaxValue);
                    break;
                }

                if ((flags & LockstepFrameRecordFlags.Joined) != 0)
                {
                    var joinData = new FixedList64Bytes<byte>();
                    var joinLength = reader.ReadByte();
                    var joinBytes = reader.ReadBytesPtr(joinLength);
                    if (joinBytes == null || joinLength > LockstepProtocol.MAX_JOIN_DATA_SIZE)
                    {
                        break;
                    }
                    joinData.AddRange(joinBytes, joinLength);

                    var created = entityManager.CreateEntity(_playerArchetype);
                    entityManager.SetComponentData(created, new LockstepPlayer { slot = slot, joinTick = time.tick, joinData = joinData });
                    entityManager.SetComponentData(created, new LockstepPlayerInput { size = info.inputSize });
                    entityManager.SetComponentEnabled<LockstepPlayerLeft>(created, false);
                    var slotBuffer = entityManager.GetBuffer<LockstepPlayerSlot>(sessionEntity);
                    slotBuffer[slot] = new LockstepPlayerSlot { player = created };
                }

                var player = entityManager.GetBuffer<LockstepPlayerSlot>(sessionEntity, true)[slot].player;
                var hasPlayer = player != Entity.Null;

                if ((flags & LockstepFrameRecordFlags.Left) != 0 && hasPlayer)
                {
                    entityManager.SetComponentEnabled<LockstepPlayerLeft>(player, true);
                }

                if ((flags & LockstepFrameRecordFlags.Input) != 0)
                {
                    var inputBytes = reader.ReadBytesPtr(info.inputSize);
                    if (inputBytes != null && hasPlayer)
                    {
                        var input = entityManager.GetComponentData<LockstepPlayerInput>(player);
                        input.SetCurrent(inputBytes, info.inputSize);
                        entityManager.SetComponentData(player, input);
                    }
                }

                if ((flags & LockstepFrameRecordFlags.Commands) != 0)
                {
                    var commandCount = reader.ReadByte();
                    for (var c = 0; c < commandCount && !reader.HasFailed; c++)
                    {
                        if (!LockstepCommandWire.TryRead(ref reader, out var typeHash, out var payload, out var size, out var data, out var dataLength))
                        {
                            break;
                        }
                        if (hasPlayer)
                        {
                            var command = LockstepCommand.FromRaw(typeHash, payload, size);
                            command.AppendData(entityManager.GetBuffer<LockstepCommandData>(player), data, dataLength);
                            entityManager.GetBuffer<LockstepCommand>(player).Add(command);
                        }
                    }
                }
            }

            if (reader.HasFailed)
            {
                UnityEngine.Debug.LogError($"[Lockstep] Malformed frame for tick {time.tick}; the rest of the frame was skipped.");
            }
        }
    }
}
