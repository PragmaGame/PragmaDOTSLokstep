using System;
using AOT;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.NetCode;

namespace Pragma.Lockstep.Netcode
{
    /// <summary>
    /// The single RPC that carries every lockstep packet over Netcode for Entities' reliable, ordered channel.
    /// </summary>
    /// <remarks>
    /// Serialization is written by hand so that only <see cref="length"/> bytes go on the wire. The protocol never
    /// produces packets above <see cref="CAPACITY"/>: larger messages are fragmented by <see cref="LockstepPacketFramer"/>.
    /// </remarks>
    [BurstCompile]
    public unsafe struct LockstepPacketRpc : IComponentData, IRpcCommandSerializer<LockstepPacketRpc>
    {
        public const int CAPACITY = LockstepProtocol.DEFAULT_MAX_PACKET_SIZE;

        public ushort length;
        private fixed byte _data[CAPACITY];

        public static LockstepPacketRpc Create(byte* data, int length)
        {
            if (length < 0 || length > CAPACITY)
            {
                throw new ArgumentOutOfRangeException(nameof(length), length, $"A lockstep packet is limited to {CAPACITY} bytes.");
            }
            var rpc = new LockstepPacketRpc { length = (ushort)length };
            UnsafeUtility.MemCpy(rpc._data, data, length);
            return rpc;
        }

        /// <summary>Pointer to the payload of an RPC that lives at a stable address (a native array element).</summary>
        public static byte* GetData(LockstepPacketRpc* rpc) => rpc->_data;

        public void Serialize(ref DataStreamWriter writer, in RpcSerializerState state, in LockstepPacketRpc data)
        {
            var copy = data;
            writer.WriteUShort(copy.length);
            writer.WriteBytesUnsafe(copy._data, copy.length);
        }

        public void Deserialize(ref DataStreamReader reader, in RpcDeserializerState state, ref LockstepPacketRpc data)
        {
            var length = reader.ReadUShort();
            if (length > CAPACITY)
            {
                // Corrupted stream: deliver an empty packet, the protocol ignores it.
                data.length = 0;
                return;
            }
            fixed (byte* destination = data._data)
            {
                reader.ReadBytesUnsafe(destination, length);
            }
            data.length = length;
        }

        [BurstCompile(DisableDirectCall = true)]
        [MonoPInvokeCallback(typeof(RpcExecutor.ExecuteDelegate))]
        private static void InvokeExecute(ref RpcExecutor.Parameters parameters)
        {
            RpcExecutor.ExecuteCreateRequestComponent<LockstepPacketRpc, LockstepPacketRpc>(ref parameters);
        }

        private static readonly PortableFunctionPointer<RpcExecutor.ExecuteDelegate> InvokeExecutePointer =
            new PortableFunctionPointer<RpcExecutor.ExecuteDelegate>(InvokeExecute);

        public PortableFunctionPointer<RpcExecutor.ExecuteDelegate> CompileExecute() => InvokeExecutePointer;
    }
}
