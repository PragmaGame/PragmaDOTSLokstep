using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// Identity of a simulation entity that is the same on every client. Add it to entities that commands or game data
    /// must refer to across machines.
    /// </summary>
    /// <remarks>
    /// <see cref="Entity"/> values cannot do this job: Entities hands out entity ids from one store shared by every
    /// world of the process, so the same simulated entity gets different ids on different clients. Never send an
    /// <see cref="Entity"/> in a command, never sort or seed randomness by it; use this id.
    /// Ids are assigned in deterministic order at the end of the tick in which the entity was created, starting at 1.
    /// </remarks>
    public struct LockstepEntityId : IComponentData
    {
        public uint value;

        public bool IsAssigned => value != 0;
    }
}
