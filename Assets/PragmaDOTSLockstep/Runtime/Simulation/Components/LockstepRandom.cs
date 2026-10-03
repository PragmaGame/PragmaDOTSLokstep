using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>Global deterministic random generator, a singleton in the simulation world, seeded by the session.</summary>
    /// <remarks>Write the value back after drawing numbers: <c>SystemAPI.GetSingletonRW&lt;LockstepRandom&gt;().ValueRW.value.NextInt(10)</c>.</remarks>
    public struct LockstepRandom : IComponentData
    {
        public FixedRandom value;
    }
}
