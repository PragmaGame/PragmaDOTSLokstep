using NUnit.Framework;
using Pragma.Lockstep.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    public unsafe class ChecksumTests
    {
        private World _a;
        private World _b;

        [SetUp]
        public void SetUp()
        {
            _a = new World("Checksum A");
            _b = new World("Checksum B");
        }

        [TearDown]
        public void TearDown()
        {
            _a.Dispose();
            _b.Dispose();
        }

        private static Entity Populate(EntityManager entityManager)
        {
            Entity last = default;
            for (var i = 0; i < 5; i++)
            {
                last = entityManager.CreateEntity(typeof(ChecksumPadded), typeof(ChecksumValue), typeof(ChecksumToggle), typeof(ChecksumElement), typeof(ChecksumDebugOnly));
                entityManager.SetComponentData(last, new ChecksumPadded { small = (byte)i, large = i * 1000 });
                entityManager.SetComponentData(last, new ChecksumValue { value = FixedPoint.FromFraction(i, 3) });
                entityManager.GetBuffer<ChecksumElement>(last).Add(new ChecksumElement { value = i });
            }
            return last;
        }

        [Test]
        public void IdenticalWorlds_HaveIdenticalChecksums()
        {
            Populate(_a.EntityManager);
            Populate(_b.EntityManager);
            AssertSameState(_a.EntityManager, _b.EntityManager);
        }

        [Test]
        public void FreshWorlds_HaveIdenticalChecksums()
        {
            AssertSameState(_a.EntityManager, _b.EntityManager);
        }

        /// <summary>Compares per type first, so a failure names the component that differs.</summary>
        internal static void AssertSameState(EntityManager a, EntityManager b)
        {
            var perTypeA = LockstepChecksum.ComputePerType(a);
            var perTypeB = LockstepChecksum.ComputePerType(b);
            foreach (var pair in perTypeA)
            {
                Assert.IsTrue(perTypeB.TryGetValue(pair.Key, out var other), $"{pair.Key} only exists in the first world");
                Assert.AreEqual(pair.Value, other, $"{pair.Key} differs");
            }
            Assert.AreEqual(perTypeA.Count, perTypeB.Count, "different component type sets");
            Assert.AreEqual(LockstepChecksum.Compute(a), LockstepChecksum.Compute(b));
        }

        [Test]
        public void ComponentValues_AffectTheChecksum()
        {
            var a = Populate(_a.EntityManager);
            var b = Populate(_b.EntityManager);
            _b.EntityManager.SetComponentData(b, new ChecksumValue { value = FixedPoint.Epsilon });
            Assert.AreNotEqual(LockstepChecksum.Compute(_a.EntityManager), LockstepChecksum.Compute(_b.EntityManager));

            _b.EntityManager.SetComponentData(b, _a.EntityManager.GetComponentData<ChecksumValue>(a));
            Assert.AreEqual(LockstepChecksum.Compute(_a.EntityManager), LockstepChecksum.Compute(_b.EntityManager));
        }

        [Test]
        public void BufferContents_AffectTheChecksum()
        {
            Populate(_a.EntityManager);
            var b = Populate(_b.EntityManager);
            _b.EntityManager.GetBuffer<ChecksumElement>(b).Add(new ChecksumElement { value = 1 });
            Assert.AreNotEqual(LockstepChecksum.Compute(_a.EntityManager), LockstepChecksum.Compute(_b.EntityManager));
        }

        [Test]
        public void EnabledState_AffectsTheChecksum()
        {
            Populate(_a.EntityManager);
            var b = Populate(_b.EntityManager);
            _b.EntityManager.SetComponentEnabled<ChecksumToggle>(b, false);
            Assert.AreNotEqual(LockstepChecksum.Compute(_a.EntityManager), LockstepChecksum.Compute(_b.EntityManager));
        }

        [Test]
        public void IgnoredComponents_DoNotAffectTheChecksum()
        {
            Populate(_a.EntityManager);
            var b = Populate(_b.EntityManager);
            _b.EntityManager.SetComponentData(b, new ChecksumDebugOnly { value = 12345 });
            Assert.AreEqual(LockstepChecksum.Compute(_a.EntityManager), LockstepChecksum.Compute(_b.EntityManager));
        }

        [Test]
        public void PresentationOnlyTypes_DoNotAffectTheChecksum()
        {
            // The editor adds a shared component to every baked entity (a player never has it): the same content baked
            // for the editor and for a player must hash alike, archetype signature included.
            Populate(_a.EntityManager);
            Populate(_b.EntityManager);
            using (var query = _b.EntityManager.CreateEntityQuery(typeof(ChecksumValue)))
            {
                _b.EntityManager.AddSharedComponent(query, new ChecksumSceneData { mask = 1UL << 59 });
            }
            AssertSameState(_a.EntityManager, _b.EntityManager);
        }

        [Test]
        public void EditorOnlyTags_DoNotAffectTheChecksum()
        {
            // Entities hides the world time entity with an internal tag only in the editor: a player build has none.
            var hideInHierarchy = typeof(World).Assembly.GetType("Unity.Entities.HideInHierarchy", true);
            Populate(_a.EntityManager);
            Populate(_b.EntityManager);
            using (var query = _b.EntityManager.CreateEntityQuery(typeof(ChecksumValue)))
            {
                _b.EntityManager.AddComponent(query, ComponentType.FromTypeIndex(TypeManager.GetTypeIndex(hideInHierarchy)));
            }
            AssertSameState(_a.EntityManager, _b.EntityManager);
        }

        [Test]
        public void TagComponents_AffectTheChecksum()
        {
            // On every entity: the chunks stay the same, only the archetype signature tells the worlds apart.
            Populate(_a.EntityManager);
            Populate(_b.EntityManager);
            using (var query = _b.EntityManager.CreateEntityQuery(typeof(ChecksumValue)))
            {
                _b.EntityManager.AddComponent<ChecksumTag>(query);
            }
            Assert.AreNotEqual(LockstepChecksum.Compute(_a.EntityManager), LockstepChecksum.Compute(_b.EntityManager));
        }

        [Test]
        public void PaddingBytes_DoNotAffectTheChecksum()
        {
            Populate(_a.EntityManager);
            Populate(_b.EntityManager);
            var size = UnsafeUtility.SizeOf<ChecksumPadded>();
            Assert.AreEqual(8, size);

            var handle = _b.EntityManager.GetComponentTypeHandle<ChecksumPadded>(false);
            using (var query = _b.EntityManager.CreateEntityQuery(typeof(ChecksumPadded)))
            using (var chunks = query.ToArchetypeChunkArray(Unity.Collections.Allocator.Temp))
            {
                var chunk = chunks[0];
                var data = (byte*)chunk.GetComponentDataPtrRW(ref handle);
                for (var i = 0; i < chunk.Count; i++)
                {
                    data[i * size + 1] = 0xAB;
                    data[i * size + 2] = 0xCD;
                    data[i * size + 3] = 0xEF;
                }
                Assert.AreEqual(LockstepChecksum.Compute(_a.EntityManager), LockstepChecksum.Compute(_b.EntityManager));

                data[4] ^= 1;
                Assert.AreNotEqual(LockstepChecksum.Compute(_a.EntityManager), LockstepChecksum.Compute(_b.EntityManager));
            }
        }

        [Test]
        public void EntityIdValues_DoNotAffectTheChecksum()
        {
            // Entity ids come from a store shared by all worlds of the process, like on different machines.
            for (var i = 0; i < 10; i++)
            {
                _b.EntityManager.DestroyEntity(_b.EntityManager.CreateEntity());
            }
            LinkFirstTwo(_a.EntityManager, Populate(_a.EntityManager), 1);
            LinkFirstTwo(_b.EntityManager, Populate(_b.EntityManager), 1);
            AssertSameState(_a.EntityManager, _b.EntityManager);
        }

        [Test]
        public void EntityReferences_AffectTheChecksum()
        {
            LinkFirstTwo(_a.EntityManager, Populate(_a.EntityManager), 1);
            LinkFirstTwo(_b.EntityManager, Populate(_b.EntityManager), 2);
            Assert.AreNotEqual(LockstepChecksum.Compute(_a.EntityManager), LockstepChecksum.Compute(_b.EntityManager));
        }

        // Adds a link from the last populated entity to the n-th entity with ChecksumValue.
        private static void LinkFirstTwo(EntityManager entityManager, Entity from, int targetIndex)
        {
            using (var query = entityManager.CreateEntityQuery(typeof(ChecksumValue)))
            using (var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                entityManager.AddComponentData(from, new ChecksumLink { target = entities[targetIndex] });
            }
        }
    }
}
