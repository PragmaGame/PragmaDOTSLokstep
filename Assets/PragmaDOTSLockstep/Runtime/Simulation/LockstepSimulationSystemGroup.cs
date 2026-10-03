using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// Root group of the deterministic simulation. It updates exactly once per lockstep tick, after the confirmed
    /// frame of that tick has been applied. Put every gameplay system here:
    /// <code>[UpdateInGroup(typeof(LockstepSimulationSystemGroup))]</code>
    /// </summary>
    /// <remarks>
    /// Rules inside this group: read time from <see cref="LockstepTime"/>, use <c>FixedPoint</c> math, use
    /// <see cref="LockstepRandom"/> or another <c>FixedRandom</c> stored in a component, keep every piece of state in
    /// components (never in system fields or statics), and play back command buffers with sort keys.
    /// </remarks>
    [WorldSystemFilter(LockstepWorldFilter.SIMULATION, LockstepWorldFilter.SIMULATION)]
    public partial class LockstepSimulationSystemGroup : ComponentSystemGroup
    {
    }
}
