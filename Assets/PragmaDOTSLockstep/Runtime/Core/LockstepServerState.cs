
namespace Pragma.Lockstep
{
    public enum LockstepServerState
    {
        /// <summary>Accepting players; no tick has been closed.</summary>
        Lobby,
        /// <summary>Ticks are being closed (the first ones may still be in the start countdown).</summary>
        Running,
        Ended,
    }
}
