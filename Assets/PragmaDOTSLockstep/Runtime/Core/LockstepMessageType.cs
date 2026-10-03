
namespace Pragma.Lockstep
{
    public enum LockstepMessageType : byte
    {
        // Client to server.
        JoinRequest = 1,
        Input = 2,
        Checksum = 3,
        Ping = 4,
        Leave = 5,

        // Server to client.
        JoinAccepted = 16,
        JoinRejected = 17,
        Start = 18,
        Frames = 19,
        InputFeedback = 20,
        Pong = 21,
        Desync = 22,
        End = 23,
    }
}
