
namespace Pragma.Lockstep
{
    /// <summary>Wire protocol constants shared by <see cref="LockstepServer"/> and <see cref="LockstepClient"/>.</summary>
    public static class LockstepProtocol
    {
        /// <summary>Bumped on every incompatible change of the messages below.</summary>
        public const ushort VERSION = 1;

        /// <summary>Player slots fit into a 64-bit mask.</summary>
        public const int MAX_PLAYERS = 64;

        /// <summary>Upper bound of the per-tick input struct.</summary>
        public const int MAX_INPUT_SIZE = 128;

        /// <summary>Capacity of <c>FixedList64Bytes&lt;byte&gt;</c>, used for join data.</summary>
        public const int MAX_JOIN_DATA_SIZE = 62;

        /// <summary>Capacity of <c>FixedList128Bytes&lt;byte&gt;</c>, used for session start data.</summary>
        public const int MAX_START_DATA_SIZE = 126;

        /// <summary>Commands one player can contribute to one tick; extra commands move to the following ticks.</summary>
        public const int MAX_COMMANDS_PER_TICK = 32;

        /// <summary>Packet size used when nothing else is configured; fits the Netcode RPC payload.</summary>
        public const int DEFAULT_MAX_PACKET_SIZE = 1024;
    }
}
