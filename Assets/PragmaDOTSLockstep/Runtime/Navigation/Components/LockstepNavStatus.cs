namespace Pragma.Lockstep.Navigation
{
    /// <summary>What a <see cref="LockstepNavAgent"/> is doing.</summary>
    public enum LockstepNavStatus : byte
    {
        /// <summary>No destination, or stopped.</summary>
        Idle,
        /// <summary>A destination is set; the path is planned in the next navigation update.</summary>
        Requested,
        /// <summary>Walking along its <see cref="LockstepNavWaypoint"/>s.</summary>
        Moving,
        /// <summary>Reached the end of its path: the destination, or the point closest to it when the path is partial.</summary>
        Arrived,
    }
}
