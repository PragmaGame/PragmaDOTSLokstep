using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>The last id handed out, part of the simulation state (a singleton in the simulation world).</summary>
    public struct LockstepEntityIdCounter : IComponentData
    {
        public uint last;
    }
}
