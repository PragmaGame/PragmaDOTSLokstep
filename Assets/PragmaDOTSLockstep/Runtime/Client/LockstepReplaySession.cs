using System;
using System.Collections.Generic;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// A replay watched in client worlds: a <see cref="LockstepReplayHost"/> and, for every world that watches, a
    /// <see cref="LockstepClient"/> over a loopback network, registered in <see cref="LockstepWorlds"/> so the world's
    /// presentation shows the replay the way it shows a match. Several worlds can watch different slots of one replay at
    /// one pace. Call <see cref="Update"/> every frame; the session owns the replay.
    /// </summary>
    public sealed class LockstepReplaySession : IDisposable
    {
        private readonly LockstepLoopbackNetwork _network = new LockstepLoopbackNetwork();
        private readonly List<(World World, LockstepClient Client)> _viewers = new List<(World, LockstepClient)>();
        private readonly LockstepReplay _replay;
        private float _speed = 1f;
        private bool _isPaused;
        private bool _disposed;

        public LockstepReplaySession(LockstepReplay replay)
        {
            _replay = replay ?? throw new ArgumentNullException(nameof(replay));
            Host = new LockstepReplayHost(replay, _network.ServerTransport);
            _network.AttachServer(Host);
        }

        public LockstepReplayHost Host { get; }

        public LockstepReplay Replay => _replay;

        /// <summary>Playback speed of the host and every watching client: 1 is real time. Never negative.</summary>
        public float Speed
        {
            get => _speed;
            set
            {
                _speed = Math.Max(0f, value);
                ApplyPace();
            }
        }

        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                _isPaused = value;
                ApplyPace();
            }
        }

        /// <summary>
        /// Watches <paramref name="slot"/> in <paramref name="world"/>: its client joins as that player, so the world sees
        /// what the player saw. A game passes the options it plays with, usually
        /// <see cref="LockstepClientWorldUtility.CreateSimulationOptions"/> of the world: the replay plays back only with
        /// the same simulation.
        /// </summary>
        public LockstepClient Watch(World world, int slot, LockstepClientSettings settings, LockstepSimulationOptions options)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LockstepReplaySession));
            }
            var connectionId = _viewers.Count + 1;
            var client = new LockstepClient(settings, _network.CreateClientTransport(connectionId), options);
            Host.Watch(connectionId, slot);
            _network.AttachClient(connectionId, client);
            LockstepWorlds.RegisterClient(world, client);
            _viewers.Add((world, client));
            ApplyPace();
            client.Join();
            return client;
        }

        public void Update(double now)
        {
            if (_disposed)
            {
                return;
            }
            _network.Deliver(now);
            foreach (var viewer in _viewers)
            {
                viewer.Client.Update(now);
            }
            _network.Deliver(now);
            Host.Update(now);
            _network.Deliver(now);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            foreach (var viewer in _viewers)
            {
                if (viewer.World.IsCreated)
                {
                    LockstepWorlds.UnregisterClient(viewer.World);
                }
                viewer.Client.Dispose();
            }
            _viewers.Clear();
            Host.Dispose();
            _replay.Dispose();
        }

        private void ApplyPace()
        {
            Host.Speed = _speed;
            Host.IsPaused = _isPaused;
            foreach (var viewer in _viewers)
            {
                viewer.Client.PlaybackSpeed = _isPaused ? 0f : _speed;
            }
        }
    }
}
