
namespace Pragma.Lockstep
{
    public enum LockstepClientState
    {
        Idle,
        /// <summary>Join request sent.</summary>
        Joining,
        /// <summary>Accepted, waiting for the match to start.</summary>
        Lobby,
        Running,
        Ended,
        Rejected,
    }
}
