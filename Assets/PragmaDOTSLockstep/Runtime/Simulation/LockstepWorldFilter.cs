using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>World filter of the deterministic simulation world.</summary>
    /// <remarks>
    /// Systems placed in <see cref="LockstepSimulationSystemGroup"/> inherit this filter, so they are created only in
    /// lockstep simulation worlds and never in the default, client or server worlds. The value is a bit that Entities
    /// does not use; put it on a system explicitly only when it cannot live inside the group.
    /// </remarks>
    public static class LockstepWorldFilter
    {
        public const WorldSystemFilterFlags SIMULATION = (WorldSystemFilterFlags)(1u << 28);
    }
}
