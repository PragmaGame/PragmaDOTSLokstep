using Pragma.Lockstep.Mathematics;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    /// <summary>A small deterministic game: players move with their input, count jumps (button edges) and commands.</summary>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
    public partial struct TestGameplaySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LockstepTime>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (_, entity) in SystemAPI.Query<RefRO<LockstepPlayer>>().WithAll<LockstepPlayerJoined>().WithEntityAccess())
            {
                commandBuffer.AddComponent(entity, new TestPlayerState());
            }
            commandBuffer.Playback(state.EntityManager);

            var time = SystemAPI.GetSingleton<LockstepTime>();
            ref var random = ref SystemAPI.GetSingletonRW<LockstepRandom>().ValueRW.value;
            foreach (var (input, commands, commandData, player) in
                     SystemAPI.Query<RefRO<LockstepPlayerInput>, DynamicBuffer<LockstepCommand>, DynamicBuffer<LockstepCommandData>, RefRW<TestPlayerState>>())
            {
                var current = input.ValueRO.Get<TestInput>();
                var previous = input.ValueRO.GetPrevious<TestInput>();
                ref var playerState = ref player.ValueRW;
                playerState.position += new FixedVector3(current.moveX, current.moveY, 0) * time.deltaTime;
                if ((current.buttons & 1) != 0 && (previous.buttons & 1) == 0)
                {
                    playerState.jumps++;
                }
                if (current.moveX != 0)
                {
                    playerState.movingTicks++;
                }
                for (var i = 0; i < commands.Length; i++)
                {
                    if (commands[i].TryGet<TestCommand>(out var command))
                    {
                        playerState.commandSum += command.value;
                        var data = commands[i].GetData<int>(commandData);
                        for (var d = 0; d < data.Length; d++)
                        {
                            playerState.commandDataCount++;
                            playerState.commandDataSum += data[d];
                            playerState.commandDataHash = playerState.commandDataHash * 31 + (uint)data[d];
                        }
                    }
                }
                playerState.randomSum += random.NextUInt(1000);
            }
        }
    }
}
