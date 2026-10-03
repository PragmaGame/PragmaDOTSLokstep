using Unity.Collections;

namespace Pragma.Lockstep
{
    /// <summary>
    /// The wire form of a command, the same in input messages, frames and replays: type hash, payload size, payload, data
    /// length (var uint) and data.
    /// </summary>
    /// <remarks>
    /// The server never decodes payloads: it checks the form and relays the bytes as they came, so a command costs the
    /// size of its payload and data, whatever the limits of the in-memory <see cref="LockstepCommand"/>.
    /// </remarks>
    internal static unsafe class LockstepCommandWire
    {
        public static void Write(ref LockstepByteWriter writer, in LockstepCommand command, byte* data, int dataLength)
        {
            writer.WriteInt(command.typeHash);
            writer.WriteByte(command.size);
            command.WritePayload(ref writer);
            writer.WriteVarUInt((uint)dataLength);
            writer.WriteBytes(data, dataLength);
        }

        /// <summary>The wire form of one command as a standalone array, for the queues of the client and the server.</summary>
        public static byte[] Encode(in LockstepCommand command, byte* data, int dataLength)
        {
            using (var buffer = new NativeList<byte>(16 + LockstepCommand.MAX_PAYLOAD_SIZE + dataLength, Allocator.Temp))
            {
                var writer = new LockstepByteWriter(buffer);
                Write(ref writer, command, data, dataLength);
                return buffer.AsArray().ToArray();
            }
        }

        /// <summary>
        /// Reads one command. Returns false, with <paramref name="reader"/> failed, when the bytes are not a command.
        /// The pointers point into the reader's data.
        /// </summary>
        public static bool TryRead(ref LockstepByteReader reader, out int typeHash, out byte* payload, out int size, out byte* data, out int dataLength)
        {
            typeHash = reader.ReadInt();
            size = reader.ReadByte();
            payload = reader.ReadBytesPtr(size);
            // A length beyond int.MaxValue turns negative and fails the read below.
            dataLength = (int)reader.ReadVarUInt();
            data = reader.ReadBytesPtr(dataLength);
            if (!reader.HasFailed && size > LockstepCommand.MAX_PAYLOAD_SIZE)
            {
                reader.Skip(int.MaxValue);
            }
            return !reader.HasFailed;
        }
    }
}
