using Pragma.Lockstep.Mathematics;
using Pragma.Lockstep.Stats;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    /// <summary>
    /// Changes stats the way gameplay does, picking from the simulation's random generator: on about every other tick one
    /// entity gets a timed modifier, has the permanent modifiers of a source applied again or removed, or gets a new base
    /// value. Only the chunk of the picked entity is written.
    /// </summary>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
    [UpdateBefore(typeof(LockstepStatSystem))]
    public partial struct TestStatSystem : ISystem
    {
        public const int STAT_COUNT = 3;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LockstepTime>();
            state.RequireForUpdate<LockstepRandom>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var tick = SystemAPI.GetSingleton<LockstepTime>().tick;
            ref var random = ref SystemAPI.GetSingletonRW<LockstepRandom>().ValueRW.value;
            if (!random.NextChance(FixedPoint.Half))
            {
                return;
            }

            var units = SystemAPI.QueryBuilder().WithAll<LockstepStat, LockstepStatModifier>().Build().ToEntityArray(Allocator.Temp);
            var unit = units[random.NextInt(units.Length)];
            var stat = random.NextInt(STAT_COUNT);
            var source = new LockstepStatSource(1, (uint)random.NextInt(3));
            var modifiers = SystemAPI.GetBuffer<LockstepStatModifier>(unit);
            switch (random.NextInt(4))
            {
                case 0:
                    modifiers.Add(LockstepStatModifier.Multiplicative(stat, random.NextFixedPoint(-FixedPoint.Half, FixedPoint.One), default,
                        tick + random.NextInt(1, 60)));
                    break;
                case 1:
                    LockstepStats.RemoveModifiers(modifiers, source);
                    modifiers.Add(LockstepStatModifier.Flat(stat, random.NextFixedPoint(-5, 5), source));
                    modifiers.Add(LockstepStatModifier.Additive(stat, random.NextFixedPoint(-FixedPoint.Half, FixedPoint.Half), source));
                    break;
                case 2:
                    LockstepStats.RemoveModifiers(modifiers, source);
                    break;
                default:
                    LockstepStats.TrySetBase(SystemAPI.GetBuffer<LockstepStat>(unit), stat, random.NextFixedPoint(1, 100));
                    break;
            }
        }
    }
}
