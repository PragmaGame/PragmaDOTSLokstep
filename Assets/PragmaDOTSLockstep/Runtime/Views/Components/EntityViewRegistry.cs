using Unity.Entities;

namespace Pragma.Lockstep.Views
{
    /// <summary>Marks a baked view catalog: the entity holds an <see cref="EntityViewPrefabElement"/> buffer.</summary>
    /// <remarks>It lives in presentation worlds only; nothing of it reaches a simulation world.</remarks>
    public struct EntityViewRegistry : IComponentData
    {
    }
}
