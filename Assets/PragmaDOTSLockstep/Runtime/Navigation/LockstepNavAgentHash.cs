using System;
using Pragma.Lockstep.Mathematics;
using Unity.Collections;

namespace Pragma.Lockstep.Navigation
{
    /// <summary>
    /// Agents sorted into square cells on the XZ plane, and by their index inside a cell: whatever lies within a cell
    /// size of a point lies in the cell of that point or in the eight around it. The order depends only on the
    /// positions and the indices, so every client visits the neighbours of an agent in the same order.
    /// </summary>
    /// <remarks>Add the agents, <see cref="Sort"/> once, then walk the cells with <see cref="GetFirst"/>.</remarks>
    internal struct LockstepNavAgentHash : IDisposable
    {
        private NativeList<Entry> _entries;
        private long _cellSize;

        public LockstepNavAgentHash(int capacity, FixedPoint cellSize, AllocatorManager.AllocatorHandle allocator)
        {
            _entries = new NativeList<Entry>(capacity, allocator);
            _cellSize = cellSize.rawValue;
        }

        /// <summary>Number of agents added.</summary>
        public int Count => _entries.Length;

        public void Add(FixedVector2 position, int index)
        {
            _entries.Add(new Entry { cell = GetKey(GetCell(position.x), GetCell(position.y)), index = index });
        }

        public void Sort()
        {
            _entries.Sort();
        }

        /// <summary>The index of the agent at an entry; entries run cell by cell.</summary>
        public int GetIndex(int entry) => _entries[entry].index;

        /// <summary>The cell coordinate of a world X or Z.</summary>
        public long GetCell(FixedPoint coordinate) => FloorDivide(coordinate.rawValue, _cellSize);

        /// <summary>The first entry of cell (x, z); its agents run on while <see cref="IsInCell"/> holds.</summary>
        public int GetFirst(long x, long z)
        {
            var key = GetKey(x, z);
            var low = 0;
            var high = _entries.Length;
            while (low < high)
            {
                var middle = (low + high) >> 1;
                if (_entries[middle].cell < key)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            return low;
        }

        public bool IsInCell(int entry, long x, long z) => entry < _entries.Length && _entries[entry].cell == GetKey(x, z);

        public void Dispose()
        {
            if (_entries.IsCreated)
            {
                _entries.Dispose();
            }
        }

        private static long GetKey(long x, long z) => (x << 32) ^ (z & 0xFFFFFFFFL);

        private static long FloorDivide(long value, long divisor)
        {
            var quotient = value / divisor;
            if (value % divisor != 0 && value < 0)
            {
                quotient--;
            }
            return quotient;
        }

        private struct Entry : IComparable<Entry>
        {
            public long cell;
            public int index;

            public int CompareTo(Entry other)
            {
                return cell != other.cell ? cell.CompareTo(other.cell) : index.CompareTo(other.index);
            }
        }
    }
}
