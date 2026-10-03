using Unity.Entities;

namespace Pragma.Lockstep.Netcode
{
    /// <summary>Create this singleton in a server world to host a lockstep session. Remove it to shut the session down.</summary>
    public struct LockstepServerConfig : IComponentData
    {
        public LockstepServerSettings settings;
    }
}
