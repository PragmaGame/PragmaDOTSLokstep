using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Pragma.Lockstep
{
    /// <summary>
    /// 64-bit hash of every entity and every unmanaged component value in a world, used for desync detection.
    /// </summary>
    /// <remarks>
    /// <para>Chunks are visited in archetype creation order, which is identical on every client that performed the same
    /// structural changes. Inside an archetype, components are visited by stable type hash, so the result does not depend
    /// on type indices, which can differ between builds.</para>
    /// <para>Entity ids are not hashed as such: Entities allocates them from one store shared by every world of the
    /// process, so the same simulated entity has different ids on different machines. Each entity is identified by its
    /// position in the traversal instead, and <see cref="Entity"/> fields inside components are replaced by that position.</para>
    /// <para>Only bytes that belong to fields are hashed: padding is skipped, as are pointers and blob asset references,
    /// whose addresses differ between machines. Managed, shared and chunk components, and types or fields marked
    /// <see cref="LockstepChecksumIgnoreAttribute"/>, are ignored, in the values and in the archetype signature alike, and
    /// so are the tags Entities adds only in the editor: an editor and a player build hash the same state alike, although
    /// the editor puts shared components on baked entities and hides its world time entity. Other tag components count
    /// through the archetype.</para>
    /// </remarks>
    public static unsafe class LockstepChecksum
    {
        private const int NULL_ENTITY = -1;
        private const int FOREIGN_ENTITY = -2;

        private sealed class TypeLayout
        {
            public bool hashed;
            public string name;
            public TypeIndex typeIndex;
            public ulong stableHash;
            public int elementSize;
            public bool isBuffer;
            public bool isEnableable;
            // Pairs of (offset, length) of plain field bytes inside one element, merged and sorted.
            public int[] ranges;
            // Offsets of Entity fields inside one element; each is hashed as a 4-byte traversal position.
            public int[] entityOffsets;
            public int hashedBytesPerElement;
            // One range covering the whole element and no entity fields: an array can be hashed in one call.
            public bool dense;
        }

        private struct EnabledBits
        {
            public ulong low;
            public ulong high;
        }

        private static readonly Dictionary<int, TypeLayout> Layouts = new Dictionary<int, TypeLayout>();

        // Entities tags the world time and system entities with these only in the editor (#if UNITY_EDITOR); they are
        // internal, hence the names.
        private static readonly HashSet<string> EditorOnlyTypes = new HashSet<string> { "Unity.Entities.HideInHierarchy" };

        public static ulong Compute(EntityManager entityManager, ulong seed = 0) => Compute(entityManager, seed, null);

        /// <summary>
        /// Hash of each component type on its own, for finding what diverged after a desync: compare the dictionaries of
        /// two clients taken at the same tick.
        /// </summary>
        public static Dictionary<string, ulong> ComputePerType(EntityManager entityManager)
        {
            var perType = new Dictionary<string, xxHash3.StreamingState>();
            Compute(entityManager, 0, perType);
            var result = new Dictionary<string, ulong>(perType.Count);
            foreach (var pair in perType)
            {
                var state = pair.Value;
                var digest = state.DigestHash64();
                result[pair.Key] = ((ulong)digest.y << 32) | digest.x;
            }
            return result;
        }

        private static ulong Compute(EntityManager entityManager, ulong seed, Dictionary<string, xxHash3.StreamingState> perType)
        {
            entityManager.CompleteAllTrackedJobs();
            var hash = new xxHash3.StreamingState(true, seed);
            var gather = new NativeList<byte>(1024, Allocator.Temp);
            var handles = new Dictionary<int, DynamicComponentTypeHandle>();
            var entityHandle = entityManager.GetEntityTypeHandle();

            using (var chunks = entityManager.GetAllChunks(Allocator.Temp))
            {
                // Pass 1: the traversal position of every entity, which replaces its id.
                var entityCount = 0;
                for (var c = 0; c < chunks.Length; c++)
                {
                    entityCount += chunks[c].Count;
                }
                var positions = new NativeHashMap<Entity, int>(math.max(1, entityCount), Allocator.Temp);
                var position = 0;
                for (var c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    for (var i = 0; i < entities.Length; i++)
                    {
                        positions.TryAdd(entities[i], position++);
                    }
                }

                // Pass 2: archetype signatures and component values.
                var archetypeCache = new Dictionary<EntityArchetype, TypeLayout[]>();
                for (var c = 0; c < chunks.Length; c++)
                {
                    var chunk = chunks[c];
                    var count = chunk.Count;
                    if (count == 0)
                    {
                        continue;
                    }

                    if (!archetypeCache.TryGetValue(chunk.Archetype, out var layouts))
                    {
                        layouts = GetArchetypeLayouts(chunk.Archetype);
                        archetypeCache.Add(chunk.Archetype, layouts);
                    }

                    hash.Update(count);
                    for (var t = 0; t < layouts.Length; t++)
                    {
                        // Types the hash ignores stay out of the signature too: content baked for the editor carries
                        // editor-only shared components, and must hash like the same content baked for a player.
                        if (layouts[t].hashed)
                        {
                            hash.Update(layouts[t].stableHash);
                        }
                    }

                    for (var t = 0; t < layouts.Length; t++)
                    {
                        var layout = layouts[t];
                        if (!layout.hashed)
                        {
                            continue;
                        }

                        if (!handles.TryGetValue(layout.typeIndex.Value, out var handle))
                        {
                            handle = entityManager.GetDynamicComponentTypeHandle(ComponentType.ReadOnly(layout.typeIndex));
                            handles.Add(layout.typeIndex.Value, handle);
                        }

                        if (layout.isEnableable)
                        {
                            MaskEnabledBits(chunk.GetEnableableBits(ref handle), count, out var bits);
                            Update(ref hash, perType, layout.name, &bits, sizeof(EnabledBits));
                        }

                        if (layout.isBuffer)
                        {
                            var accessor = chunk.GetUntypedBufferAccessor(ref handle);
                            for (var i = 0; i < count; i++)
                            {
                                var data = (byte*)accessor.GetUnsafeReadOnlyPtrAndLength(i, out var length);
                                Update(ref hash, perType, layout.name, &length, sizeof(int));
                                GetHashedBytes(ref gather, layout, positions, data, length, out var bytes, out var byteCount);
                                Update(ref hash, perType, layout.name, bytes, byteCount);
                            }
                        }
                        else if (layout.elementSize > 0)
                        {
                            var data = (byte*)chunk.GetComponentDataPtrRO(ref handle);
                            GetHashedBytes(ref gather, layout, positions, data, count, out var bytes, out var byteCount);
                            Update(ref hash, perType, layout.name, bytes, byteCount);
                        }
                    }
                }
            }

            var digest = hash.DigestHash64();
            return ((ulong)digest.y << 32) | digest.x;
        }

        private static void Update(ref xxHash3.StreamingState hash, Dictionary<string, xxHash3.StreamingState> perType, string name, void* data, int length)
        {
            if (length <= 0)
            {
                return;
            }
            hash.Update(data, length);
            if (perType == null)
            {
                return;
            }
            if (!perType.TryGetValue(name, out var state))
            {
                state = new xxHash3.StreamingState(true);
            }
            state.Update(data, length);
            perType[name] = state;
        }

        // The bytes that stand for the elements' state: the data itself when the type has no padding and no entity
        // fields, otherwise field bytes and entity positions gathered into a scratch buffer.
        private static void GetHashedBytes(ref NativeList<byte> gather, TypeLayout layout, NativeHashMap<Entity, int> positions, byte* data, int count, out byte* bytes, out int length)
        {
            bytes = data;
            length = 0;
            if (count <= 0 || layout.hashedBytesPerElement == 0)
            {
                return;
            }
            if (layout.dense)
            {
                length = count * layout.elementSize;
                return;
            }

            gather.ResizeUninitialized(count * layout.hashedBytesPerElement);
            var destination = gather.GetUnsafePtr();
            var ranges = layout.ranges;
            var entityOffsets = layout.entityOffsets;
            for (var i = 0; i < count; i++)
            {
                var element = data + i * layout.elementSize;
                for (var r = 0; r < ranges.Length; r += 2)
                {
                    UnsafeUtility.MemCpy(destination, element + ranges[r], ranges[r + 1]);
                    destination += ranges[r + 1];
                }
                for (var e = 0; e < entityOffsets.Length; e++)
                {
                    var entity = *(Entity*)(element + entityOffsets[e]);
                    var value = entity == Entity.Null ? NULL_ENTITY : positions.TryGetValue(entity, out var index) ? index : FOREIGN_ENTITY;
                    *(int*)destination = value;
                    destination += sizeof(int);
                }
            }
            bytes = gather.GetUnsafePtr();
            length = gather.Length;
        }

        // Bits past the entity count are not state; mask them out.
        private static void MaskEnabledBits(Unity.Burst.Intrinsics.v128 bits, int count, out EnabledBits masked)
        {
            masked.low = bits.ULong0;
            masked.high = bits.ULong1;
            if (count < 64)
            {
                masked.low &= (1UL << count) - 1;
                masked.high = 0;
            }
            else if (count < 128)
            {
                masked.high &= (1UL << (count - 64)) - 1;
            }
        }

        private static TypeLayout[] GetArchetypeLayouts(EntityArchetype archetype)
        {
            using (var types = archetype.GetComponentTypes(Allocator.Temp))
            {
                var layouts = new List<TypeLayout>(types.Length);
                for (var i = 0; i < types.Length; i++)
                {
                    // The entity column is represented by the traversal order itself.
                    if (types[i].TypeIndex == TypeManager.GetTypeIndex<Entity>())
                    {
                        continue;
                    }
                    layouts.Add(GetLayout(types[i].TypeIndex));
                }
                layouts.Sort((a, b) => a.stableHash.CompareTo(b.stableHash));
                return layouts.ToArray();
            }
        }

        private static TypeLayout GetLayout(TypeIndex typeIndex)
        {
            if (Layouts.TryGetValue(typeIndex.Value, out var cached))
            {
                return cached;
            }

            var info = TypeManager.GetTypeInfo(typeIndex);
            var type = TypeManager.GetType(typeIndex);
            var layout = new TypeLayout
            {
                typeIndex = typeIndex,
                stableHash = info.StableTypeHash,
                name = type != null ? type.FullName : info.StableTypeHash.ToString(),
                isBuffer = typeIndex.IsBuffer,
                isEnableable = typeIndex.IsEnableable,
                elementSize = typeIndex.IsBuffer ? info.ElementSize : (typeIndex.IsZeroSized ? 0 : info.TypeSize),
                ranges = Array.Empty<int>(),
                entityOffsets = Array.Empty<int>(),
            };
            layout.hashed = type != null
                            && type.IsValueType
                            && !typeIndex.IsSharedComponentType
                            && !typeIndex.IsChunkComponent
                            && !type.IsDefined(typeof(LockstepChecksumIgnoreAttribute), false)
                            && !EditorOnlyTypes.Contains(type.FullName);

            if (layout.hashed && layout.elementSize > 0)
            {
                var ranges = new List<int2>();
                var entityOffsets = new List<int>();
                CollectRanges(type, 0, ranges, entityOffsets);
                layout.ranges = MergeRanges(ranges, layout.elementSize, out var fieldBytes);
                layout.entityOffsets = entityOffsets.ToArray();
                layout.hashedBytesPerElement = fieldBytes + entityOffsets.Count * sizeof(int);
                layout.dense = entityOffsets.Count == 0 && fieldBytes == layout.elementSize && layout.ranges.Length == 2;
            }

            Layouts.Add(typeIndex.Value, layout);
            return layout;
        }

        // Collects (offset, length) of every primitive field, recursively, and the offsets of Entity fields.
        // Pointers, blob references and fields marked [LockstepChecksumIgnore] are skipped.
        private static void CollectRanges(Type type, int baseOffset, List<int2> ranges, List<int> entityOffsets)
        {
            if (type == typeof(Entity))
            {
                entityOffsets.Add(baseOffset);
                return;
            }
            if (type.IsPrimitive || type.IsEnum)
            {
                if (type == typeof(IntPtr) || type == typeof(UIntPtr))
                {
                    return;
                }
                ranges.Add(new int2(baseOffset, UnsafeUtility.SizeOf(type)));
                return;
            }
            if (type.IsPointer || !type.IsValueType)
            {
                return;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(BlobAssetReference<>))
            {
                return;
            }

            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (var i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (field.IsDefined(typeof(LockstepChecksumIgnoreAttribute), false))
                {
                    continue;
                }
                var offset = baseOffset + UnsafeUtility.GetFieldOffset(field);
                var fixedBuffer = field.GetCustomAttribute<System.Runtime.CompilerServices.FixedBufferAttribute>();
                if (fixedBuffer != null)
                {
                    ranges.Add(new int2(offset, UnsafeUtility.SizeOf(fixedBuffer.ElementType) * fixedBuffer.Length));
                    continue;
                }
                CollectRanges(field.FieldType, offset, ranges, entityOffsets);
            }
        }

        private static int[] MergeRanges(List<int2> ranges, int elementSize, out int totalBytes)
        {
            ranges.Sort((a, b) => a.x.CompareTo(b.x));
            var merged = new List<int2>();
            foreach (var range in ranges)
            {
                var start = math.max(range.x, 0);
                var end = math.min(range.x + range.y, elementSize);
                if (end <= start)
                {
                    continue;
                }
                if (merged.Count > 0 && start <= merged[merged.Count - 1].x + merged[merged.Count - 1].y)
                {
                    var last = merged[merged.Count - 1];
                    var lastEnd = math.max(last.x + last.y, end);
                    merged[merged.Count - 1] = new int2(last.x, lastEnd - last.x);
                }
                else
                {
                    merged.Add(new int2(start, end - start));
                }
            }

            totalBytes = 0;
            var result = new int[merged.Count * 2];
            for (var i = 0; i < merged.Count; i++)
            {
                result[i * 2] = merged[i].x;
                result[i * 2 + 1] = merged[i].y;
                totalBytes += merged[i].y;
            }
            return result;
        }
    }
}
