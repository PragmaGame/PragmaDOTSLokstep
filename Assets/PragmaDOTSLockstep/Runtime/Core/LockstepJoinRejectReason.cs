
namespace Pragma.Lockstep
{
    public enum LockstepJoinRejectReason : byte
    {
        None = 0,
        InvalidRequest = 1,
        ProtocolMismatch = 2,
        SimulationMismatch = 3,
        SessionFull = 4,
        GameInProgress = 5,
        GameEnded = 6,
    }
}
