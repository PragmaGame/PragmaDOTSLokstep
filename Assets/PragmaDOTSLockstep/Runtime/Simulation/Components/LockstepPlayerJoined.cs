using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>Enabled only during the tick the player joined.</summary>
    public struct LockstepPlayerJoined : IComponentData, IEnableableComponent
    {
    }
}
