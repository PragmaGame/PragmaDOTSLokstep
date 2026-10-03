using System;
using System.Collections.Generic;
using Unity.Collections;

namespace Pragma.Lockstep
{
    /// <summary>
    /// Splits messages into packets that fit the transport and joins them back on the other side.
    /// </summary>
    /// <remarks>
    /// Each packet starts with one header byte: a complete message, a fragment, or the last fragment.
    /// The channel is reliable and ordered, so the fragments of one message always arrive back to back.
    /// </remarks>
    public sealed unsafe class LockstepPacketFramer : IDisposable
    {
        private const byte COMPLETE = 0;
        private const byte FRAGMENT = 1;
        private const byte LAST_FRAGMENT = 2;

        private sealed class Reassembly
        {
            public NativeList<byte> buffer;
            // The buffer holds a returned message that must stay valid until the next packet.
            public bool holdsCompletedMessage;
        }

        private readonly int _maxPacketSize;
        private readonly Dictionary<int, Reassembly> _reassembly = new Dictionary<int, Reassembly>();
        private NativeList<byte> _packet;

        public LockstepPacketFramer(int maxPacketSize)
        {
            if (maxPacketSize < 16)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPacketSize), maxPacketSize, "A packet must hold at least 16 bytes.");
            }
            _maxPacketSize = maxPacketSize;
            _packet = new NativeList<byte>(maxPacketSize, Allocator.Persistent);
        }

        public int MaxPacketSize => _maxPacketSize;

        /// <summary>The largest message that still travels as a single packet.</summary>
        public int MaxUnfragmentedMessageSize => _maxPacketSize - 1;

        public void Send(ILockstepTransport transport, int connectionId, byte* message, int length)
        {
            var maxPayload = _maxPacketSize - 1;
            if (length <= maxPayload)
            {
                SendPacket(transport, connectionId, COMPLETE, message, length);
                return;
            }

            for (var offset = 0; offset < length; offset += maxPayload)
            {
                var size = Math.Min(maxPayload, length - offset);
                var header = offset + size >= length ? LAST_FRAGMENT : FRAGMENT;
                SendPacket(transport, connectionId, header, message + offset, size);
            }
        }

        /// <summary>
        /// Feeds one received packet. Returns true when it completes a message. The message pointer stays valid
        /// until the next call for the same connection.
        /// </summary>
        /// <returns>False while a fragmented message is incomplete, or when the packet is malformed.</returns>
        public bool Receive(int connectionId, byte* packet, int length, out byte* message, out int messageLength)
        {
            message = null;
            messageLength = 0;

            _reassembly.TryGetValue(connectionId, out var state);
            if (state != null && state.holdsCompletedMessage)
            {
                state.buffer.Clear();
                state.holdsCompletedMessage = false;
            }

            if (packet == null || length < 1)
            {
                return false;
            }

            var header = packet[0];
            var payload = packet + 1;
            var payloadLength = length - 1;

            switch (header)
            {
                case COMPLETE:
                    // A complete packet in the middle of a fragmented message means the stream is corrupted:
                    // drop the partial message rather than glue unrelated bytes together.
                    if (state != null)
                    {
                        state.buffer.Clear();
                    }
                    message = payload;
                    messageLength = payloadLength;
                    return true;

                case FRAGMENT:
                case LAST_FRAGMENT:
                    if (state == null)
                    {
                        state = new Reassembly { buffer = new NativeList<byte>(_maxPacketSize * 2, Allocator.Persistent) };
                        _reassembly.Add(connectionId, state);
                    }
                    if (payloadLength > 0)
                    {
                        state.buffer.AddRange(payload, payloadLength);
                    }
                    if (header == FRAGMENT)
                    {
                        return false;
                    }
                    state.holdsCompletedMessage = true;
                    message = state.buffer.GetUnsafePtr();
                    messageLength = state.buffer.Length;
                    return true;

                default:
                    if (state != null)
                    {
                        state.buffer.Clear();
                    }
                    return false;
            }
        }

        /// <summary>Releases the reassembly state of a closed connection.</summary>
        public void RemoveConnection(int connectionId)
        {
            if (_reassembly.TryGetValue(connectionId, out var state))
            {
                state.buffer.Dispose();
                _reassembly.Remove(connectionId);
            }
        }

        public void Dispose()
        {
            foreach (var state in _reassembly.Values)
            {
                state.buffer.Dispose();
            }
            _reassembly.Clear();
            if (_packet.IsCreated)
            {
                _packet.Dispose();
            }
        }

        private void SendPacket(ILockstepTransport transport, int connectionId, byte header, byte* payload, int length)
        {
            _packet.Clear();
            _packet.Add(header);
            if (length > 0)
            {
                _packet.AddRange(payload, length);
            }
            transport.Send(connectionId, _packet.GetUnsafePtr(), _packet.Length);
        }
    }
}
