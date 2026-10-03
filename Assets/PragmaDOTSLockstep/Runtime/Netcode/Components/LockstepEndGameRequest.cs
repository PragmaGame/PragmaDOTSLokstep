using Unity.Entities;

namespace Pragma.Lockstep.Netcode
{
    /// <summary>Create an entity with this component in the server world to end the match.</summary>
    public struct LockstepEndGameRequest : IComponentData
    {
        public LockstepEndReason reason;
    }
}
