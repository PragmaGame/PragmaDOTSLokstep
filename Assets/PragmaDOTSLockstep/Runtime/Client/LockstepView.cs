using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>Put on view entities spawned by <see cref="LockstepViewSystem"/>; points at the simulated entity.</summary>
    public struct LockstepView : IComponentData
    {
        public Entity simulationEntity;
    }
}
