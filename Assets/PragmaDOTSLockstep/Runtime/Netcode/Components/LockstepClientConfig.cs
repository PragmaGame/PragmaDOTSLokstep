using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep.Netcode
{
    /// <summary>
    /// Create this singleton in a client world to take part in the lockstep session of the server it connects to.
    /// The client joins as soon as the connection is established.
    /// </summary>
    public struct LockstepClientConfig : IComponentData
    {
        public LockstepClientSettings settings;
        /// <summary>Game-defined data that reaches the simulation in <see cref="LockstepPlayer.joinData"/>.</summary>
        public FixedList64Bytes<byte> joinData;
        /// <summary>Wait until a prefab registry exists in the client world before creating the simulation.</summary>
        public bool waitForPrefabRegistry;
    }
}
