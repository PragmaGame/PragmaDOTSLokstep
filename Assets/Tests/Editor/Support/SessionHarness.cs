using System;
using System.Collections.Generic;
using Unity.Collections;

namespace Pragma.Lockstep.Tests
{
    /// <summary>A server and clients over a loopback network, stepped with simulated time.</summary>
    internal sealed class SessionHarness : IDisposable
    {
        private readonly List<int> _connectionIds = new List<int>();
        private int _nextConnectionId = 1;

        public SessionHarness(LockstepServerSettings settings, double latency = 0, double jitter = 0, int seed = 1)
        {
            Network = new LockstepLoopbackNetwork(seed) { Latency = latency, Jitter = jitter };
            Server = new LockstepServer(settings, Network.ServerTransport);
            Network.AttachServer(Server);
            Server.DesyncDetectedEvent += report => Desyncs.Add(report);
        }

        public LockstepLoopbackNetwork Network { get; }
        public LockstepServer Server { get; }
        public List<LockstepClient> Clients { get; } = new List<LockstepClient>();
        public List<LockstepDesyncReport> Desyncs { get; } = new List<LockstepDesyncReport>();
        public double Time { get; private set; } = 10;
        public int Frame { get; private set; }

        public LockstepClient AddClient(LockstepSimulationOptions options = null, LockstepClientSettings? settings = null, FixedList64Bytes<byte> joinData = default)
        {
            var connectionId = _nextConnectionId++;
            var client = new LockstepClient(settings ?? LockstepClientSettings.Default, Network.CreateClientTransport(connectionId),
                options ?? TestUtility.Options(typeof(TestGameplaySystem)));
            Network.SetTime(Time);
            Network.AttachClient(connectionId, client);
            Clients.Add(client);
            _connectionIds.Add(connectionId);
            client.Join(joinData);
            return client;
        }

        public void Disconnect(LockstepClient client)
        {
            var index = Clients.IndexOf(client);
            Network.DetachClient(_connectionIds[index]);
            Clients.RemoveAt(index);
            _connectionIds.RemoveAt(index);
            client.Dispose();
        }

        /// <summary>One frame: clients send input, the network delivers, the server closes ticks.</summary>
        public void Step(double deltaTime, Action<LockstepClient, int> setInput = null)
        {
            Time += deltaTime;
            Frame++;
            Network.Deliver(Time);
            foreach (var client in Clients)
            {
                setInput?.Invoke(client, Frame);
                client.Update(Time);
            }
            Network.Deliver(Time);
            Server.Update(Time);
            Network.Deliver(Time);
        }

        /// <summary>Advances only the server, as if every client had frozen.</summary>
        public void StepServerOnly(double seconds, double deltaTime = 0.01)
        {
            var end = Time + seconds;
            while (Time < end)
            {
                Time += deltaTime;
                Network.Deliver(Time);
                Server.Update(Time);
                Network.Deliver(Time);
            }
        }

        /// <summary>Runs for a duration with frame times varying between 8 and 25 ms.</summary>
        public void Run(double seconds, Action<LockstepClient, int> setInput = null, int seed = 3)
        {
            var random = new System.Random(seed + Frame);
            var end = Time + seconds;
            while (Time < end)
            {
                Step(0.008 + random.NextDouble() * 0.017, setInput);
            }
        }

        public void Dispose()
        {
            foreach (var client in Clients)
            {
                client.Dispose();
            }
            Clients.Clear();
            Server.Dispose();
        }
    }
}
