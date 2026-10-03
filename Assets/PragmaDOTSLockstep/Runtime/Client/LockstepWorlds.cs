using System.Collections.Generic;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// Finds the sessions run by the lockstep systems of a world: the client of a client or offline world, the server of
    /// a server world.
    /// </summary>
    /// <remarks>Entries are keyed by <see cref="World.SequenceNumber"/> and removed when the owning system shuts down.</remarks>
    public static class LockstepWorlds
    {
        private static readonly Dictionary<ulong, LockstepClient> Clients = new Dictionary<ulong, LockstepClient>();
        private static readonly Dictionary<ulong, LockstepServer> Servers = new Dictionary<ulong, LockstepServer>();

        /// <summary>The session running in <paramref name="world"/> (a client or offline world), if any.</summary>
        public static bool TryGetClient(World world, out LockstepClient client)
        {
            client = null;
            return world != null && world.IsCreated && Clients.TryGetValue(world.SequenceNumber, out client);
        }

        /// <summary>The simulation world of the session running in <paramref name="world"/>, if it exists yet.</summary>
        public static bool TryGetSimulation(World world, out LockstepSimulation simulation)
        {
            simulation = TryGetClient(world, out var client) ? client.Simulation : null;
            return simulation != null;
        }

        /// <summary>
        /// The server running in <paramref name="world"/>: the session server of a server world, or the in-process
        /// server of an offline world.
        /// </summary>
        public static bool TryGetServer(World world, out LockstepServer server)
        {
            server = null;
            return world != null && world.IsCreated && Servers.TryGetValue(world.SequenceNumber, out server);
        }

        /// <summary>Adds every world that runs a client session to <paramref name="results"/>.</summary>
        public static void GetClients(List<(World World, LockstepClient Client)> results)
        {
            foreach (var world in World.All)
            {
                if (world.IsCreated && Clients.TryGetValue(world.SequenceNumber, out var client))
                {
                    results.Add((world, client));
                }
            }
        }

        /// <summary>Adds every world that runs a server (including offline in-process servers) to <paramref name="results"/>.</summary>
        public static void GetServers(List<(World World, LockstepServer Server)> results)
        {
            foreach (var world in World.All)
            {
                if (world.IsCreated && Servers.TryGetValue(world.SequenceNumber, out var server))
                {
                    results.Add((world, server));
                }
            }
        }

        public static void RegisterClient(World world, LockstepClient client) => Clients[world.SequenceNumber] = client;

        public static void UnregisterClient(World world) => Clients.Remove(world.SequenceNumber);

        public static void RegisterServer(World world, LockstepServer server) => Servers[world.SequenceNumber] = server;

        public static void UnregisterServer(World world) => Servers.Remove(world.SequenceNumber);

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Clients.Clear();
            Servers.Clear();
        }
    }
}
