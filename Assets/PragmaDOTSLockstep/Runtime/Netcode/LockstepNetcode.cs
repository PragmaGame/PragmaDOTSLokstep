using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;

namespace Pragma.Lockstep.Netcode
{
    /// <summary>Shortcuts for setting up lockstep sessions on top of Netcode for Entities worlds.</summary>
    public static class LockstepNetcode
    {
        /// <summary>Hosts a lockstep session in a server world (replaces the configuration if one exists).</summary>
        public static void HostSession(World serverWorld, in LockstepServerSettings settings)
        {
            SetSingleton(serverWorld.EntityManager, new LockstepServerConfig { settings = settings });
        }

        /// <summary>Makes a client world join the lockstep session of the server it is (or will be) connected to.</summary>
        public static void JoinSession(World clientWorld, in LockstepClientSettings settings, in FixedList64Bytes<byte> joinData = default, bool waitForPrefabRegistry = false)
        {
            SetSingleton(clientWorld.EntityManager, new LockstepClientConfig
            {
                settings = settings,
                joinData = joinData,
                waitForPrefabRegistry = waitForPrefabRegistry,
            });
        }

        /// <summary>Starts listening on the server world's network driver.</summary>
        public static bool Listen(World serverWorld, NetworkEndpoint endpoint)
        {
            using (var query = serverWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
            {
                return query.GetSingletonRW<NetworkStreamDriver>().ValueRW.Listen(endpoint);
            }
        }

        /// <summary>Connects a client world to a server; returns the connection entity.</summary>
        public static Entity Connect(World clientWorld, NetworkEndpoint endpoint)
        {
            using (var query = clientWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
            {
                return query.GetSingletonRW<NetworkStreamDriver>().ValueRW.Connect(clientWorld.EntityManager, endpoint);
            }
        }

        /// <summary>The server session running in <paramref name="serverWorld"/>, if any.</summary>
        public static bool TryGetServer(World serverWorld, out LockstepServer server) => LockstepWorlds.TryGetServer(serverWorld, out server);

        /// <summary>The client session running in <paramref name="clientWorld"/>, if any.</summary>
        public static bool TryGetClient(World clientWorld, out LockstepClient client) => LockstepWorlds.TryGetClient(clientWorld, out client);

        /// <summary>Starts the match on the next server update, whoever has joined.</summary>
        public static void RequestStart(World serverWorld)
        {
            serverWorld.EntityManager.CreateEntity(ComponentType.ReadWrite<LockstepStartGameRequest>());
        }

        /// <summary>Ends the match on the next server update.</summary>
        public static void RequestEnd(World serverWorld, LockstepEndReason reason = LockstepEndReason.Finished)
        {
            var entity = serverWorld.EntityManager.CreateEntity(ComponentType.ReadWrite<LockstepEndGameRequest>());
            serverWorld.EntityManager.SetComponentData(entity, new LockstepEndGameRequest { reason = reason });
        }

        private static void SetSingleton<T>(EntityManager entityManager, T value) where T : unmanaged, IComponentData
        {
            using (var query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<T>()))
            {
                if (query.CalculateEntityCount() == 0)
                {
                    entityManager.CreateSingleton(value);
                }
                else
                {
                    query.SetSingleton(value);
                }
            }
        }
    }
}
