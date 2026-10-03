namespace Pragma.Lockstep.Navigation
{
    /// <summary>Result of <see cref="LockstepPathfinder"/>.</summary>
    public enum LockstepPathStatus : byte
    {
        /// <summary>No path: the grid is invalid or no cell of it is walkable.</summary>
        Failed,
        /// <summary>The path ends at the destination.</summary>
        Complete,
        /// <summary>The destination is blocked or out of reach; the path ends at the reachable point closest to it.</summary>
        Partial,
    }
}
