using Unity.Entities;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// Navigation of one tick, inside <see cref="LockstepSimulationSystemGroup"/>: obstacles update the grid, agents plan
    /// or check their paths, then walk. Set destinations from systems that update before this group, and agents start
    /// walking on the same tick.
    /// </summary>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
    public partial class LockstepNavSystemGroup : ComponentSystemGroup
    {
    }
}
