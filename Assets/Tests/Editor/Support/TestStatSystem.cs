using Pragma.Lockstep.Mathematics;
using Pragma.Lockstep.Stats;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    /// <summary>
    /// Changes stats the way gameplay does, picking from the simulation's random generator: on about every other tick one
    /// entity gets a timed modifier, has the permanent modifiers of a source applied again or removed, gets a new base value
    /// or a change of a resource, or the grantor gives or takes grants. Only the chunk of the picked entity is written.
    /// </summary>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
    [UpdateBefore(typeof(LockstepStatSystem))]
    public partial struct TestStatSystem : ISystem
    {
        public const int FIRST_ATTRIBUTE = 1;
        public const int ATTRIBUTE_COUNT = 3;
        public const int FIRST_RESOURCE = FIRST_ATTRIBUTE + ATTRIBUTE_COUNT;
        public const int RESOURCE_COUNT = 3;
        public const int TARGET_COUNT = 3;

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
            var grantor = SystemAPI.QueryBuilder().WithAll<LockstepStatGrant>().Build().GetSingletonEntity();
            var unit = units[random.NextInt(units.Length)];
            var attribute = FIRST_ATTRIBUTE + random.NextInt(ATTRIBUTE_COUNT);
            var source = new LockstepStatSource(1, (uint)random.NextInt(3));
            var stacking = random.NextChance(FixedPoint.Half) ? LockstepStatStacking.Strongest : LockstepStatStacking.Stack;
            var modifiers = SystemAPI.GetBuffer<LockstepStatModifier>(unit);
            switch (random.NextInt(6))
            {
                // Percentages reach -100 % and below, so some factors stop at zero.
                case 0:
                    modifiers.Add(LockstepStatModifier.MultiplicativePercent(attribute, random.NextFixedPoint(-FixedPoint.FromFraction(3, 2), FixedPoint.One),
                        source, tick + random.NextInt(1, 60), stacking));
                    break;
                case 1:
                    modifiers.RemoveModifiers(source);
                    modifiers.Add(LockstepStatModifier.Flat(attribute, random.NextFixedPoint(-5, 5), source, LockstepStatModifier.PERMANENT, stacking));
                    modifiers.Add(LockstepStatModifier.AdditivePercent(attribute, random.NextFixedPoint(-FixedPoint.One, FixedPoint.Half), source));
                    break;
                case 2:
                    modifiers.RemoveModifiers(source);
                    break;
                case 3:
                    SystemAPI.GetBuffer<LockstepStat>(unit).TrySetBase(attribute, random.NextFixedPoint(1, 100));
                    break;
                case 4:
                    // Mostly damage, so resources are seldom full.
                    SystemAPI.GetBuffer<LockstepStatChange>(unit).Add(LockstepStatChange.Create(FIRST_RESOURCE + random.NextInt(RESOURCE_COUNT),
                        random.NextFixedPoint(-20, 10), source));
                    break;
                default:
                {
                    var grants = SystemAPI.GetBuffer<LockstepStatGrant>(grantor);
                    if (random.NextChance(FixedPoint.Half))
                    {
                        var endTick = random.NextChance(FixedPoint.Half) ? tick + random.NextInt(1, 60) : LockstepStatModifier.PERMANENT;
                        var modifier = LockstepStatModifier.AdditivePercent(attribute, random.NextFixedPoint(-FixedPoint.Half, FixedPoint.Half), source,
                            endTick, stacking);
                        grants.Add(LockstepStatGrant.Create(modifier, random.NextInt(TARGET_COUNT + 1)));
                    }
                    else
                    {
                        grants.RemoveGrants(source);
                    }
                    break;
                }
            }
        }
    }
}
