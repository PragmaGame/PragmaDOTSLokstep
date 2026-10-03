using System;
using System.Collections.Generic;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>Creation options of a <see cref="LockstepSimulation"/>.</summary>
    public sealed class LockstepSimulationOptions
    {
        public string WorldName { get; set; } = "Lockstep Simulation";

        /// <summary>Adds every system that belongs to <see cref="LockstepSimulationSystemGroup"/> (default).</summary>
        public bool AutoDiscoverSystems { get; set; } = true;

        /// <summary>Extra systems, typically <c>[DisableAutoCreation]</c> ones used by tests or optional features.</summary>
        public IReadOnlyList<Type> AdditionalSystems { get; set; }

        /// <summary>
        /// Runs once after the world and its systems exist and before tick 0, for example to copy prefabs in.
        /// It must do exactly the same thing on every client.
        /// </summary>
        public Action<World> Initialize { get; set; }

        /// <summary>
        /// Optional gate checked before the world is created, for example "the prefab registry subscene has loaded".
        /// Frames keep buffering while it returns false; the client catches up afterwards.
        /// </summary>
        public Func<bool> CanCreate { get; set; }
    }
}
