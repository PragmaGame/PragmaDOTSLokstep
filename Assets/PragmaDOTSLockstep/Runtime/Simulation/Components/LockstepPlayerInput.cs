using System;
using System.Diagnostics;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>The confirmed input of a player for the current tick, plus the input of the previous tick.</summary>
    /// <remarks>
    /// Read it as the game's input struct with <see cref="Get{T}"/>. Comparing with <see cref="GetPrevious{T}"/>
    /// gives button edges (pressed or released this tick) that are correct however the frames arrived.
    /// </remarks>
    public unsafe struct LockstepPlayerInput : IComponentData
    {
        public const int CAPACITY = LockstepProtocol.MAX_INPUT_SIZE;

        private fixed byte _current[CAPACITY];
        private fixed byte _previous[CAPACITY];

        /// <summary>Input size of the session, in bytes.</summary>
        public int size;

        public T Get<T>() where T : unmanaged
        {
            CheckSize(UnsafeUtility.SizeOf<T>(), size);
            fixed (byte* p = _current)
            {
                return UnsafeUtility.ReadArrayElement<T>(p, 0);
            }
        }

        public T GetPrevious<T>() where T : unmanaged
        {
            CheckSize(UnsafeUtility.SizeOf<T>(), size);
            fixed (byte* p = _previous)
            {
                return UnsafeUtility.ReadArrayElement<T>(p, 0);
            }
        }

        /// <summary>True when the input differs from the previous tick.</summary>
        public bool HasChanged()
        {
            fixed (byte* current = _current)
            fixed (byte* previous = _previous)
            {
                return UnsafeUtility.MemCmp(current, previous, size) != 0;
            }
        }

        internal void SetCurrent(byte* data, int size)
        {
            fixed (byte* current = _current)
            {
                UnsafeUtility.MemCpy(current, data, size);
            }
        }

        internal void ShiftCurrentToPrevious()
        {
            fixed (byte* current = _current)
            fixed (byte* previous = _previous)
            {
                UnsafeUtility.MemCpy(previous, current, CAPACITY);
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private static void CheckSize(int typeSize, int sessionSize)
        {
            if (typeSize > sessionSize)
            {
                throw new InvalidOperationException($"The input struct is {typeSize} bytes but the session input size is {sessionSize} bytes. Configure LockstepServerSettings.InputSize with the size of your input struct.");
            }
        }
    }
}
