using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Pragma.Lockstep
{
    /// <summary>Reads and writes unmanaged structs from and to byte lists.</summary>
    public static unsafe class LockstepBytes
    {
        public static T Read<T>(in FixedList64Bytes<byte> bytes) where T : unmanaged
        {
            var value = default(T);
            var size = Math.Min(UnsafeUtility.SizeOf<T>(), bytes.Length);
            fixed (FixedList64Bytes<byte>* list = &bytes)
            {
                UnsafeUtility.MemCpy(&value, list->GetUnsafeReadOnlyPtr(), size);
            }
            return value;
        }

        public static T Read<T>(in FixedList128Bytes<byte> bytes) where T : unmanaged
        {
            var value = default(T);
            var size = Math.Min(UnsafeUtility.SizeOf<T>(), bytes.Length);
            fixed (FixedList128Bytes<byte>* list = &bytes)
            {
                UnsafeUtility.MemCpy(&value, list->GetUnsafeReadOnlyPtr(), size);
            }
            return value;
        }

        public static FixedList64Bytes<byte> ToFixedList64<T>(in T value) where T : unmanaged
        {
            var size = UnsafeUtility.SizeOf<T>();
            if (size > LockstepProtocol.MAX_JOIN_DATA_SIZE)
            {
                throw new ArgumentException($"{typeof(T)} is {size} bytes; join data is limited to {LockstepProtocol.MAX_JOIN_DATA_SIZE}.");
            }
            var list = new FixedList64Bytes<byte>();
            var copy = value;
            list.AddRange(&copy, size);
            return list;
        }

        public static FixedList128Bytes<byte> ToFixedList128<T>(in T value) where T : unmanaged
        {
            var size = UnsafeUtility.SizeOf<T>();
            if (size > LockstepProtocol.MAX_START_DATA_SIZE)
            {
                throw new ArgumentException($"{typeof(T)} is {size} bytes; start data is limited to {LockstepProtocol.MAX_START_DATA_SIZE}.");
            }
            var list = new FixedList128Bytes<byte>();
            var copy = value;
            list.AddRange(&copy, size);
            return list;
        }
    }
}
