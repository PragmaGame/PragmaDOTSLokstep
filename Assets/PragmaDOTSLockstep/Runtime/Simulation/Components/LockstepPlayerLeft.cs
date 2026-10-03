using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>Enabled during the tick the player left. The player entity is destroyed at the start of the next tick.</summary>
    public struct LockstepPlayerLeft : IComponentData, IEnableableComponent
    {
    }
}
