using System;
using System.Collections.Generic;
using Pragma.Lockstep.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Core;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// A deterministic simulation world: an isolated Entities <see cref="World"/> that only changes through
    /// <see cref="Step"/>, one confirmed frame at a time.
    /// </summary>
    /// <remarks>
    /// The world is never added to the player loop. Nothing but the lockstep systems may create entities in it,
    /// which is what keeps entity ids, chunk layout and iteration order identical on every client.
    /// </remarks>
    public sealed unsafe class LockstepSimulation : IDisposable
    {
        private static ulong _defaultSimulationHash;

        private readonly double _tickSeconds;
        private readonly Entity _sessionEntity;
        private bool _disposed;

        public LockstepSimulation(in LockstepSessionConfig config, LockstepSimulationOptions options = null)
        {
            config.Validate();
            options ??= new LockstepSimulationOptions();
            Config = config;
            _tickSeconds = 1.0 / config.TickRate;

            World = new World(options.WorldName, WorldFlags.Simulation);
            try
            {
                var entityManager = World.EntityManager;
                _sessionEntity = entityManager.CreateEntity(
                    ComponentType.ReadWrite<LockstepTime>(),
                    ComponentType.ReadWrite<LockstepSessionInfo>(),
                    ComponentType.ReadWrite<LockstepRandom>(),
                    ComponentType.ReadWrite<LockstepEntityIdCounter>(),
                    ComponentType.ReadWrite<LockstepFrameData>(),
                    ComponentType.ReadWrite<LockstepPlayerSlot>());
                entityManager.SetComponentData(_sessionEntity, CreateTime(0));
                entityManager.SetComponentData(_sessionEntity, new LockstepSessionInfo
                {
                    seed = config.Seed,
                    tickRate = config.TickRate,
                    maxPlayers = config.MaxPlayers,
                    inputSize = config.InputSize,
                    startData = config.StartData,
                });
                entityManager.SetComponentData(_sessionEntity, new LockstepRandom { value = new FixedRandom(config.Seed) });
                entityManager.GetBuffer<LockstepPlayerSlot>(_sessionEntity).Resize(config.MaxPlayers, NativeArrayOptions.ClearMemory);

                DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(World, CollectSystemTypes(options));
                options.Initialize?.Invoke(World);
            }
            catch
            {
                World.Dispose();
                throw;
            }
        }

        public World World { get; }
        public LockstepSessionConfig Config { get; }

        /// <summary>Number of simulated ticks, which is also the next tick to simulate.</summary>
        public int Tick { get; private set; }

        /// <summary>Simulates the next tick with its confirmed frame.</summary>
        public void Step(byte* frame, int length)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LockstepSimulation));
            }

            var entityManager = World.EntityManager;
            var buffer = entityManager.GetBuffer<LockstepFrameData>(_sessionEntity);
            buffer.ResizeUninitialized(length);
            if (length > 0)
            {
                UnsafeUtility.MemCpy(buffer.GetUnsafePtr(), frame, length);
            }
            entityManager.SetComponentData(_sessionEntity, CreateTime(Tick));

            // Built-in time is kept consistent too, but gameplay must read LockstepTime.
            World.SetTime(new TimeData(Tick * _tickSeconds, (float)_tickSeconds));
            World.Unmanaged.ResetUpdateAllocator();
            World.Update();
            entityManager.CompleteAllTrackedJobs();
            Tick++;
        }

        public void Step(NativeArray<byte> frame) => Step((byte*)frame.GetUnsafeReadOnlyPtr(), frame.Length);

        /// <summary>Hash of the whole world state after the latest tick; see <see cref="LockstepChecksum"/>.</summary>
        public ulong ComputeChecksum() => LockstepChecksum.Compute(World.EntityManager);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (World.IsCreated)
            {
                World.Dispose();
            }
        }

        /// <summary>
        /// Hash of the protocol version and of every auto-discovered simulation system. Clients send it when joining
        /// so a server can refuse a build whose simulation differs.
        /// </summary>
        public static ulong DefaultSimulationHash
        {
            get
            {
                if (_defaultSimulationHash == 0)
                {
                    var hash = TypeHash.FNV1A64(LockstepProtocol.VERSION.ToString());
                    foreach (var type in SortByName(DefaultWorldInitialization.GetAllSystems(LockstepWorldFilter.SIMULATION)))
                    {
                        hash = TypeHash.CombineFNV1A64(hash, TypeHash.FNV1A64(type.FullName));
                    }
                    _defaultSimulationHash = hash == 0 ? 1 : hash;
                }
                return _defaultSimulationHash;
            }
        }

        private LockstepTime CreateTime(int tick)
        {
            return new LockstepTime
            {
                tick = tick,
                tickRate = Config.TickRate,
                deltaTime = Config.DeltaTime,
                elapsedTime = FixedPoint.FromFraction(tick, Config.TickRate),
            };
        }

        private static List<Type> CollectSystemTypes(LockstepSimulationOptions options)
        {
            var types = new HashSet<Type>
            {
                typeof(LockstepSimulationSystemGroup),
                typeof(LockstepFrameApplySystem),
                typeof(LockstepTransformHistorySystem),
                typeof(LockstepBeginSimulationEntityCommandBufferSystem),
                typeof(LockstepEndSimulationEntityCommandBufferSystem),
                typeof(LockstepEntityIdSystem),
            };
            if (options.AutoDiscoverSystems)
            {
                types.UnionWith(DefaultWorldInitialization.GetAllSystems(LockstepWorldFilter.SIMULATION));
            }
            if (options.AdditionalSystems != null)
            {
                types.UnionWith(options.AdditionalSystems);
            }

            // Creation order decides the ids of the entities systems create, so it must not depend on assembly load
            // order: sort by name first, then let the [CreateBefore]/[CreateAfter] rules reorder deterministically.
            var sorted = SortByName(types);
            TypeManager.SortSystemTypesInCreationOrder(sorted);
            return sorted;
        }

        private static List<Type> SortByName(IEnumerable<Type> types)
        {
            var list = new List<Type>(types);
            list.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            return list;
        }
    }
}
