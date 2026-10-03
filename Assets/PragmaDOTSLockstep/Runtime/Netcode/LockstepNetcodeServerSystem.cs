using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.NetCode;

namespace Pragma.Lockstep.Netcode
{
    /// <summary>
    /// Runs a <see cref="LockstepServer"/> in a Netcode server world while a <see cref="LockstepServerConfig"/> exists.
    /// </summary>
    /// <remarks>
    /// Every connection entity gets its own lockstep connection id. Packets travel as <see cref="LockstepPacketRpc"/>.
    /// The system runs in the server simulation group, so ticks are closed on the server's fixed tick rate; keep the
    /// lockstep tick rate at or below <c>ClientServerTickRate.SimulationTickRate</c>.
    /// </remarks>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NetworkReceiveSystemGroup))]
    public partial class LockstepNetcodeServerSystem : SystemBase
    {
        private sealed unsafe class Transport : ILockstepTransport
        {
            public EntityManager entityManager;
            public EntityArchetype archetype;
            public NativeHashMap<int, Entity> connections;

            public void Send(int connectionId, byte* data, int length)
            {
                if (!connections.TryGetValue(connectionId, out var connection))
                {
                    return;
                }
                var entity = entityManager.CreateEntity(archetype);
                entityManager.SetComponentData(entity, LockstepPacketRpc.Create(data, length));
                entityManager.SetComponentData(entity, new SendRpcCommandRequest { TargetConnection = connection });
            }
        }

        private LockstepServer _server;
        private Transport _transport;
        private Entity _handle;
        private EntityQuery _connectionQuery;
        private EntityQuery _receivedQuery;
        private EntityQuery _startRequestQuery;
        private EntityQuery _endRequestQuery;
        private NativeHashMap<int, Entity> _connectionsById;
        private NativeHashMap<Entity, int> _idsByConnection;
        private readonly List<int> _removed = new List<int>();
        private int _lastConnectionId;
        private readonly List<int> _violations = new List<int>();

        /// <summary>The running server, or null.</summary>
        public LockstepServer Server => _server;

        protected override void OnCreate()
        {
            _connectionQuery = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamConnection>());
            _receivedQuery = GetEntityQuery(ComponentType.ReadOnly<LockstepPacketRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            _startRequestQuery = GetEntityQuery(ComponentType.ReadOnly<LockstepStartGameRequest>());
            _endRequestQuery = GetEntityQuery(ComponentType.ReadOnly<LockstepEndGameRequest>());
            _connectionsById = new NativeHashMap<int, Entity>(16, Allocator.Persistent);
            _idsByConnection = new NativeHashMap<Entity, int>(16, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            Shutdown();
            _connectionsById.Dispose();
            _idsByConnection.Dispose();
        }

        protected override unsafe void OnUpdate()
        {
            var hasConfig = SystemAPI.TryGetSingleton<LockstepServerConfig>(out var config);
            if (!hasConfig && _server != null)
            {
                Shutdown();
            }
            if (hasConfig && _server == null)
            {
                Startup(config.settings);
            }
            if (_server == null)
            {
                // Not hosting: still consume stray lockstep packets so they do not pile up.
                if (!_receivedQuery.IsEmptyIgnoreFilter)
                {
                    EntityManager.DestroyEntity(_receivedQuery);
                }
                return;
            }

            var now = SystemAPI.Time.ElapsedTime;
            RefreshConnections();

            if (!_receivedQuery.IsEmptyIgnoreFilter)
            {
                var packets = _receivedQuery.ToComponentDataArray<LockstepPacketRpc>(Allocator.Temp);
                var requests = _receivedQuery.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                EntityManager.DestroyEntity(_receivedQuery);
                var packetPtr = (LockstepPacketRpc*)packets.GetUnsafeReadOnlyPtr();
                for (var i = 0; i < packets.Length; i++)
                {
                    if (_idsByConnection.TryGetValue(requests[i].SourceConnection, out var connectionId))
                    {
                        _server.OnPacket(connectionId, LockstepPacketRpc.GetData(packetPtr + i), packets[i].length, now);
                    }
                }
            }

            if (!_startRequestQuery.IsEmptyIgnoreFilter)
            {
                EntityManager.DestroyEntity(_startRequestQuery);
                _server.StartGame(now);
            }
            if (!_endRequestQuery.IsEmptyIgnoreFilter)
            {
                var reason = _endRequestQuery.ToComponentDataArray<LockstepEndGameRequest>(Allocator.Temp)[0].reason;
                EntityManager.DestroyEntity(_endRequestQuery);
                _server.EndGame(reason);
            }

            _server.Update(now);

            foreach (var connectionId in _violations)
            {
                if (_connectionsById.TryGetValue(connectionId, out var connection) && EntityManager.Exists(connection))
                {
                    EntityManager.AddComponentData(connection, new NetworkStreamRequestDisconnect { Reason = NetworkStreamDisconnectReason.InvalidRpc });
                }
            }
            _violations.Clear();

            SystemAPI.SetSingleton(new LockstepServerStatus
            {
                state = _server.State,
                closedTicks = _server.ClosedTicks,
                playerCount = _server.PlayerCount,
            });
        }

        private void Startup(in LockstepServerSettings settings)
        {
            _transport = new Transport
            {
                entityManager = EntityManager,
                archetype = EntityManager.CreateArchetype(ComponentType.ReadWrite<LockstepPacketRpc>(), ComponentType.ReadWrite<SendRpcCommandRequest>()),
                connections = _connectionsById,
            };
            var effective = settings;
            if (effective.MaxPacketSize <= 0 || effective.MaxPacketSize > LockstepPacketRpc.CAPACITY)
            {
                effective.MaxPacketSize = LockstepPacketRpc.CAPACITY;
            }
            _server = new LockstepServer(effective, _transport);
            _server.ProtocolViolationEvent += (connectionId, _) => _violations.Add(connectionId);
            _connectionsById.Clear();
            _idsByConnection.Clear();

            _handle = EntityManager.CreateEntity(ComponentType.ReadWrite<LockstepServerStatus>());
            EntityManager.SetName(_handle, "LockstepServer");
            LockstepWorlds.RegisterServer(World, _server);
        }

        private void Shutdown()
        {
            if (_server == null)
            {
                return;
            }
            LockstepWorlds.UnregisterServer(World);
            _server.EndGame(LockstepEndReason.ServerShutdown);
            _server.Dispose();
            _server = null;
            _transport = null;
            if (EntityManager.Exists(_handle))
            {
                EntityManager.DestroyEntity(_handle);
            }
            _handle = Entity.Null;
        }

        // Connections are tracked by entity rather than NetworkId, which netcode may hand to a new connection.
        // A connection entity that is gone means a disconnect.
        private void RefreshConnections()
        {
            var entities = _connectionQuery.ToEntityArray(Allocator.Temp);
            var alive = new NativeHashSet<Entity>(entities.Length + 1, Allocator.Temp);
            for (var i = 0; i < entities.Length; i++)
            {
                alive.Add(entities[i]);
            }

            _removed.Clear();
            foreach (var pair in _idsByConnection)
            {
                if (!alive.Contains(pair.Key))
                {
                    _removed.Add(pair.Value);
                }
            }
            foreach (var connectionId in _removed)
            {
                if (_connectionsById.TryGetValue(connectionId, out var entity))
                {
                    _idsByConnection.Remove(entity);
                }
                _connectionsById.Remove(connectionId);
                _server.OnDisconnected(connectionId);
            }

            for (var i = 0; i < entities.Length; i++)
            {
                if (_idsByConnection.ContainsKey(entities[i]))
                {
                    continue;
                }
                var connectionId = ++_lastConnectionId;
                _idsByConnection.Add(entities[i], connectionId);
                _connectionsById.Add(connectionId, entities[i]);
                _server.OnConnected(connectionId);
            }
        }
    }
}
