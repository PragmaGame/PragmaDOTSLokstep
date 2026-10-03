using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    /// <summary>Breaks determinism on purpose: only added to one client in desync tests.</summary>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
    [UpdateAfter(typeof(TestGameplaySystem))]
    public partial struct TestDesyncSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            foreach (var player in SystemAPI.Query<RefRW<TestPlayerState>>())
            {
                player.ValueRW.jumps += 1000;
            }
        }
    }
}
