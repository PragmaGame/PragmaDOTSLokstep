using Unity.Entities;

namespace Pragma.Lockstep.Netcode
{
    /// <summary>Create an entity with this component in the server world to start the match now.</summary>
    public struct LockstepStartGameRequest : IComponentData
    {
    }
}
