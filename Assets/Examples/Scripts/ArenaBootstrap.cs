using System.Collections.Generic;
using System.IO;
using Pragma.Lockstep.Netcode;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine;

namespace Pragma.Lockstep.Examples
{
    /// <summary>
    /// Starts the sample in one of three ways: offline (in-process server), host (server and client worlds in this
    /// process) or join (a client world connecting to an address). The scene disables Netcode's automatic bootstrap so
    /// the worlds are only created on demand.
    /// </summary>
    public sealed class ArenaBootstrap : MonoBehaviour
    {
        private enum Mode
        {
            None,
            Offline,
            Host,
            Client,
        }

        [SerializeField] private string _address = "127.0.0.1";
        [SerializeField] private ushort _port = 7979;
        [SerializeField] private int _tickRate = 30;
        [SerializeField] private int _maxPlayers = 8;

        private readonly List<World> _createdWorlds = new List<World>();
        private Mode _mode;
        private string _status = "";

        private void OnDestroy() => Stop();

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 330), GUI.skin.box);
            GUILayout.Label("Pragma DOTS Lockstep — Arena");
            if (_mode == Mode.None)
            {
                DrawMenu();
            }
            else
            {
                DrawSession();
            }
            if (!string.IsNullOrEmpty(_status))
            {
                GUILayout.Label(_status);
            }
            GUILayout.EndArea();
        }

        private void DrawMenu()
        {
            GUILayout.Label("WASD / stick: move, Space: shoot, Shift: dash, C: color");
            if (GUILayout.Button("Play offline"))
            {
                StartOffline();
            }
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Address", GUILayout.Width(55));
            _address = GUILayout.TextField(_address);
            GUILayout.Label("Port", GUILayout.Width(30));
            ushort.TryParse(GUILayout.TextField(_port.ToString(), GUILayout.Width(50)), out _port);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Host (server + client)"))
            {
                StartHost();
            }
            if (GUILayout.Button("Join"))
            {
                StartClient();
            }
        }

        private void DrawSession()
        {
            var clients = new List<(World World, LockstepClient Client)>();
            LockstepWorlds.GetClients(clients);
            var servers = new List<(World World, LockstepServer Server)>();
            LockstepWorlds.GetServers(servers);

            GUILayout.Label($"Mode: {_mode}");
            foreach (var (serverWorld, server) in servers)
            {
                if (_mode == Mode.Offline)
                {
                    continue;
                }
                GUILayout.Label($"Server: {server.State}, players {server.PlayerCount}, tick {server.ClosedTicks}");
                if (server.State == LockstepServerState.Lobby && GUILayout.Button("Start match now"))
                {
                    LockstepNetcode.RequestStart(serverWorld);
                }
            }
            foreach (var (_, client) in clients)
            {
                GUILayout.Label($"Client: {client.State}, slot {client.LocalSlot + 1}");
                GUILayout.Label($"Ticks: {client.SimulatedTicks} simulated / {client.ConfirmedTicks} confirmed");
                if (!double.IsNaN(client.RoundTripTime))
                {
                    GUILayout.Label($"RTT {client.RoundTripTime * 1000:0} ms, jitter {client.JitterTicks:0.0} ticks");
                }
                if (client.IsDesynced)
                {
                    GUILayout.Label($"DESYNC at tick {client.DesyncTick}!");
                }
                if (client.Settings.RecordReplay && client.State != LockstepClientState.Joining && GUILayout.Button("Save replay"))
                {
                    var path = Path.Combine(Application.persistentDataPath, $"arena-{System.DateTime.Now:yyyyMMdd-HHmmss}.lockstep");
                    File.WriteAllBytes(path, client.ExportReplay());
                    _status = $"Saved {path}";
                }
            }
            if (GUILayout.Button("Leave"))
            {
                Stop();
            }
        }

        private void StartOffline()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                _status = "No default world to run the offline session in.";
                return;
            }
            var config = LockstepOfflineConfig.Create<ArenaInput>(_tickRate);
            config.checksumInterval = 0;
            world.EntityManager.CreateSingleton(config);
            _mode = Mode.Offline;
            _status = "";
        }

        private void StartHost()
        {
            Application.runInBackground = true;
            var server = ClientServerBootstrap.CreateServerWorld("Arena Server");
            var client = ClientServerBootstrap.CreateClientWorld("Arena Client");
            _createdWorlds.Add(server);
            _createdWorlds.Add(client);

            LockstepNetcode.HostSession(server, CreateServerSettings());
            LockstepNetcode.JoinSession(client, LockstepClientSettings.Default);
            if (!LockstepNetcode.Listen(server, NetworkEndpoint.AnyIpv4.WithPort(_port)))
            {
                _status = $"Could not listen on port {_port}.";
                Stop();
                return;
            }
            LockstepNetcode.Connect(client, NetworkEndpoint.LoopbackIpv4.WithPort(_port));
            _mode = Mode.Host;
            _status = $"Hosting on port {_port}. Other players join with your address.";
        }

        private void StartClient()
        {
            if (!NetworkEndpoint.TryParse(_address, _port, out var endpoint))
            {
                _status = $"'{_address}' is not a valid address.";
                return;
            }
            Application.runInBackground = true;
            var client = ClientServerBootstrap.CreateClientWorld("Arena Client");
            _createdWorlds.Add(client);
            LockstepNetcode.JoinSession(client, LockstepClientSettings.Default);
            LockstepNetcode.Connect(client, endpoint);
            _mode = Mode.Client;
            _status = $"Connecting to {endpoint}…";
        }

        private LockstepServerSettings CreateServerSettings()
        {
            var settings = LockstepServerSettings.Default;
            settings.TickRate = _tickRate;
            settings.MaxPlayers = _maxPlayers;
            settings.InputSize = UnsafeUtility.SizeOf<ArenaInput>();
            settings.MinPlayersToStart = 1;
            settings.AllowLateJoin = true;
            settings.ChecksumInterval = 30;
            return settings;
        }

        private void Stop()
        {
            if (_mode == Mode.Offline)
            {
                var world = World.DefaultGameObjectInjectionWorld;
                if (world != null && world.IsCreated)
                {
                    using (var query = world.EntityManager.CreateEntityQuery(typeof(LockstepOfflineConfig)))
                    {
                        world.EntityManager.DestroyEntity(query);
                    }
                }
            }
            foreach (var world in _createdWorlds)
            {
                if (world.IsCreated)
                {
                    world.Dispose();
                }
            }
            _createdWorlds.Clear();
            _mode = Mode.None;
        }
    }
}
