namespace Pragma.Lockstep
{
    /// <summary>
    /// The server side of a session as a transport sees it: connection events and incoming packets.
    /// <see cref="LockstepServer"/> runs a live match, <see cref="LockstepReplayHost"/> plays a recorded one to the same
    /// clients.
    /// </summary>
    public unsafe interface ILockstepServerEndpoint
    {
        void OnConnected(int connectionId);

        void OnDisconnected(int connectionId);

        void OnPacket(int connectionId, byte* data, int length, double now);
    }
}
