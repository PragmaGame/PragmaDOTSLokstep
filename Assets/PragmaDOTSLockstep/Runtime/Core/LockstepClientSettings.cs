using System;
using UnityEngine;

namespace Pragma.Lockstep
{
    /// <summary>Configuration of a <see cref="LockstepClient"/>.</summary>
    [Serializable]
    public struct LockstepClientSettings
    {
        /// <summary>Run the simulation; thin clients and bots that only send input turn it off.</summary>
        [field: SerializeField] public bool Simulate { get; set; }
        /// <summary>How many ticks before its deadline an input should reach the server.</summary>
        [field: SerializeField] public float InputMarginTicks { get; set; }
        /// <summary>Confirmed frames kept in reserve before display, absorbing network jitter.</summary>
        [field: SerializeField] public float PlayoutDelayTicks { get; set; }
        /// <summary>Upper bound of the playout delay once the measured jitter is added.</summary>
        [field: SerializeField] public float MaxPlayoutDelayTicks { get; set; }
        /// <summary>Ticks simulated at most per update while catching up.</summary>
        [field: SerializeField] public int MaxTicksPerUpdate { get; set; }
        /// <summary>Time budget per update while catching up; at least one tick always runs.</summary>
        [field: SerializeField] public float MaxSimulationMillisecondsPerUpdate { get; set; }
        [field: SerializeField] public float PingIntervalSeconds { get; set; }
        /// <summary>Keep every frame so the match can be exported with <see cref="LockstepClient.ExportReplay"/>.</summary>
        [field: SerializeField] public bool RecordReplay { get; set; }
        /// <summary>Stop simulating after the server reports a desync.</summary>
        [field: SerializeField] public bool StopOnDesync { get; set; }
        [field: SerializeField] public int MaxPacketSize { get; set; }

        public static LockstepClientSettings Default => new LockstepClientSettings
        {
            Simulate = true,
            InputMarginTicks = 1.5f,
            PlayoutDelayTicks = 1f,
            MaxPlayoutDelayTicks = 10f,
            MaxTicksPerUpdate = 60,
            MaxSimulationMillisecondsPerUpdate = 16f,
            PingIntervalSeconds = 0.5f,
            RecordReplay = true,
            StopOnDesync = false,
            MaxPacketSize = LockstepProtocol.DEFAULT_MAX_PACKET_SIZE,
        };
    }
}
