using Unity.Entities;

namespace Pragma.Lockstep.Netcode
{
    /// <summary>Read-only status of the server session, a singleton in the server world.</summary>
    public struct LockstepServerStatus : IComponentData
    {
        public LockstepServerState state;
        public int closedTicks;
        public int playerCount;
    }
}
