using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// Runs an in-process <see cref="LockstepServer"/> and <see cref="LockstepClient"/> over a loopback network while
    /// a <see cref="LockstepOfflineConfig"/> exists. The path is the same as online, so offline play exercises the
    /// whole protocol.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(LockstepInputSystemGroup))]
    public partial class LockstepOfflineSystem : SystemBase
    {
        private const int LOCAL_CONNECTION_ID = 1;

        private LockstepLoopbackNetwork _network;
        private LockstepServer _server;
        private LockstepClient _client;
        private EntityQuery _localInputQuery;

        public LockstepClient Client => _client;
        public LockstepServer Server => _server;

        protected override void OnCreate()
        {
            _localInputQuery = GetEntityQuery(ComponentType.ReadWrite<LockstepLocalInput>(), ComponentType.ReadWrite<LockstepCommand>());
            RequireForUpdate<LockstepOfflineConfig>();
        }

        protected override void OnUpdate()
        {
            var now = SystemAPI.Time.ElapsedTime;
            if (_client == null)
            {
                StartSession(SystemAPI.GetSingleton<LockstepOfflineConfig>(), now);
            }

            LockstepClientWorldUtility.PushLocalInput(EntityManager, _localInputQuery, _client);
            _network.Deliver(now);
            _client.Update(now);
            _network.Deliver(now);
            _server.Update(now);
            _network.Deliver(now);
        }

        protected override void OnStopRunning() => StopSession();

        protected override void OnDestroy() => StopSession();

        private void StartSession(LockstepOfflineConfig config, double now)
        {
            var serverSettings = LockstepServerSettings.Default;
            serverSettings.TickRate = config.tickRate;
            serverSettings.MaxPlayers = config.maxPlayers > 0 ? config.maxPlayers : 1;
            serverSettings.InputSize = config.inputSize;
            serverSettings.Seed = config.seed;
            serverSettings.ChecksumInterval = config.checksumInterval;
            serverSettings.StartData = config.startData;
            serverSettings.MinPlayersToStart = 1;
            serverSettings.StartDelaySeconds = 0f;
            serverSettings.AllowLateJoin = false;

            var clientSettings = LockstepClientSettings.Default;
            clientSettings.InputMarginTicks = 0.5f;
            clientSettings.PlayoutDelayTicks = 0.5f;

            _network = new LockstepLoopbackNetwork();
            _network.SetTime(now);
            _server = new LockstepServer(serverSettings, _network.ServerTransport);
            _network.AttachServer(_server);
            var options = LockstepClientWorldUtility.CreateSimulationOptions(World, config.waitForPrefabRegistry);
            _client = new LockstepClient(clientSettings, _network.CreateClientTransport(LOCAL_CONNECTION_ID), options);
            _network.AttachClient(LOCAL_CONNECTION_ID, _client);
            LockstepWorlds.RegisterClient(World, _client);
            LockstepWorlds.RegisterServer(World, _server);
            _client.Join(config.joinData);
        }

        private void StopSession()
        {
            if (_client == null)
            {
                return;
            }
            LockstepWorlds.UnregisterClient(World);
            LockstepWorlds.UnregisterServer(World);
            _client.Dispose();
            _server.Dispose();
            _client = null;
            _server = null;
            _network = null;
        }
    }
}
