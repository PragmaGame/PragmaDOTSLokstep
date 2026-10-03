namespace Pragma.Lockstep
{
    /// <summary>
    /// Sends packets for <see cref="LockstepServer"/> and <see cref="LockstepClient"/>.
    /// </summary>
    /// <remarks>
    /// The protocol expects a reliable, ordered channel per connection (Netcode RPCs, TCP, a loopback queue).
    /// Packets never exceed the max packet size the server or client was created with; larger messages are
    /// fragmented before they reach the transport. The data pointer is only valid during the call.
    /// </remarks>
    public unsafe interface ILockstepTransport
    {
        /// <param name="connectionId">Target connection; clients always send to the server and may ignore it.</param>
        void Send(int connectionId, byte* data, int length);
    }
}
