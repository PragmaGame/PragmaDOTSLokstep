using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;

namespace Pragma.Lockstep
{
    /// <summary>
    /// In-process network between one server endpoint (<see cref="LockstepServer"/>, <see cref="LockstepReplayHost"/>) and
    /// any number of <see cref="LockstepClient"/>s. Used for offline play, replays and tests; latency and jitter are
    /// simulated, order per connection is preserved.
    /// </summary>
    public sealed unsafe class LockstepLoopbackNetwork
    {
        private sealed class Packet
        {
            public double deliveryTime;
            public long sequence;
            public int connectionId;
            public bool toServer;
            public byte[] data;
        }

        private sealed class Endpoint : ILockstepTransport
        {
            private readonly LockstepLoopbackNetwork _network;
            private readonly bool _toServer;
            private readonly int _clientConnectionId;

            public Endpoint(LockstepLoopbackNetwork network, bool toServer, int clientConnectionId)
            {
                _network = network;
                _toServer = toServer;
                _clientConnectionId = clientConnectionId;
            }

            public void Send(int connectionId, byte* data, int length)
            {
                var bytes = new byte[length];
                fixed (byte* destination = bytes)
                {
                    UnsafeUtility.MemCpy(destination, data, length);
                }
                _network.Enqueue(_toServer, _toServer ? _clientConnectionId : connectionId, bytes);
            }
        }

        private readonly List<Packet> _inFlight = new List<Packet>();
        private readonly Dictionary<long, double> _lastDelivery = new Dictionary<long, double>();
        private readonly Dictionary<int, LockstepClient> _clients = new Dictionary<int, LockstepClient>();
        private readonly Random _random;
        private ILockstepServerEndpoint _server;
        private long _sequence;
        private double _now;

        public LockstepLoopbackNetwork(int randomSeed = 1)
        {
            _random = new Random(randomSeed);
            ServerTransport = new Endpoint(this, false, -1);
        }

        /// <summary>One-way latency in seconds.</summary>
        public double Latency { get; set; }
        /// <summary>Extra random one-way delay in [0, Jitter) seconds; packet order is still preserved.</summary>
        public double Jitter { get; set; }

        /// <summary>The transport to create the server with.</summary>
        public ILockstepTransport ServerTransport { get; }

        public int PacketsInFlight => _inFlight.Count;

        public void AttachServer(ILockstepServerEndpoint server) => _server = server;

        /// <summary>The transport to create a client with; <paramref name="connectionId"/> identifies it on the server.</summary>
        public ILockstepTransport CreateClientTransport(int connectionId) => new Endpoint(this, true, connectionId);

        public void AttachClient(int connectionId, LockstepClient client)
        {
            _clients[connectionId] = client;
            _server?.OnConnected(connectionId);
        }

        /// <summary>Drops the connection: packets in flight are lost and the server sees a disconnect.</summary>
        public void DetachClient(int connectionId)
        {
            _clients.Remove(connectionId);
            _inFlight.RemoveAll(packet => packet.connectionId == connectionId);
            _server?.OnDisconnected(connectionId);
        }

        /// <summary>Delivers every packet due at <paramref name="now"/>, including replies sent while delivering.</summary>
        public void Deliver(double now)
        {
            _now = now;
            while (_inFlight.Count > 0 && _inFlight[0].deliveryTime <= now)
            {
                var packet = _inFlight[0];
                _inFlight.RemoveAt(0);
                fixed (byte* data = packet.data)
                {
                    if (packet.toServer)
                    {
                        _server?.OnPacket(packet.connectionId, data, packet.data.Length, now);
                    }
                    else if (_clients.TryGetValue(packet.connectionId, out var client))
                    {
                        client.OnPacket(data, packet.data.Length, now);
                    }
                }
            }
        }

        private void Enqueue(bool toServer, int connectionId, byte[] data)
        {
            var delay = Latency + (Jitter > 0 ? _random.NextDouble() * Jitter : 0);
            var channel = ((long)connectionId << 1) | (toServer ? 1L : 0L);
            var deliveryTime = _now + delay;
            if (_lastDelivery.TryGetValue(channel, out var last) && last > deliveryTime)
            {
                deliveryTime = last;
            }
            _lastDelivery[channel] = deliveryTime;

            var packet = new Packet
            {
                deliveryTime = deliveryTime,
                sequence = _sequence++,
                connectionId = connectionId,
                toServer = toServer,
                data = data,
            };
            // Keep the list sorted by delivery time; the sequence breaks ties in send order.
            var index = _inFlight.Count;
            while (index > 0 && _inFlight[index - 1].deliveryTime > deliveryTime)
            {
                index--;
            }
            _inFlight.Insert(index, packet);
        }

        /// <summary>Sets the time used to stamp packets sent outside of <see cref="Deliver"/>.</summary>
        public void SetTime(double now) => _now = Math.Max(_now, now);
    }
}
