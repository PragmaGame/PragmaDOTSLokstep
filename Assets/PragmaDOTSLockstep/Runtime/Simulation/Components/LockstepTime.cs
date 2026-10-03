using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>Simulation clock, a singleton in the simulation world. Use it instead of <c>SystemAPI.Time</c>.</summary>
    public struct LockstepTime : IComponentData
    {
        /// <summary>The tick being simulated, starting at zero.</summary>
        public int tick;
        public int tickRate;
        /// <summary>One tick in seconds.</summary>
        public FixedPoint deltaTime;
        /// <summary>Time at the start of this tick: exactly tick / tickRate.</summary>
        public FixedPoint elapsedTime;
    }
}
