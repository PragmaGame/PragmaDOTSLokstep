using Pragma.Lockstep.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep.Examples
{
    /// <summary>Turns each player's input and commands into movement, dashes, shots and color changes.</summary>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
    [UpdateAfter(typeof(ArenaPlayerLifecycleSystem))]
    [BurstCompile]
    public partial struct ArenaControlSystem : ISystem
    {
        private struct Shot
        {
            public int owner;
            public FixedVector3 position;
            public FixedVector2 velocity;
        }

        private EntityArchetype _projectileArchetype;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _projectileArchetype = state.EntityManager.CreateArchetype(stackalloc ComponentType[]
            {
                ComponentType.ReadWrite<LockstepTransform>(),
                ComponentType.ReadWrite<LockstepTransformPrevious>(),
                ComponentType.ReadWrite<ArenaProjectile>(),
            });
            state.RequireForUpdate<LockstepTime>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var time = SystemAPI.GetSingleton<LockstepTime>();
            var maxPlayers = SystemAPI.GetSingleton<LockstepSessionInfo>().maxPlayers;

            // Slot to avatar, filled in query order, which is the same on every client.
            var avatarsBySlot = new NativeArray<Entity>(maxPlayers, Allocator.Temp);
            foreach (var (avatar, entity) in SystemAPI.Query<RefRO<ArenaAvatar>>().WithEntityAccess())
            {
                avatarsBySlot[avatar.ValueRO.slot] = entity;
            }

            var avatars = SystemAPI.GetComponentLookup<ArenaAvatar>();
            var transforms = SystemAPI.GetComponentLookup<LockstepTransform>(true);
            var shots = new NativeList<Shot>(Allocator.Temp);

            foreach (var (player, input, commands) in SystemAPI.Query<RefRO<LockstepPlayer>, RefRO<LockstepPlayerInput>, DynamicBuffer<LockstepCommand>>())
            {
                var entity = avatarsBySlot[player.ValueRO.slot];
                if (entity == Entity.Null)
                {
                    continue;
                }

                var current = input.ValueRO.Get<ArenaInput>();
                var previous = input.ValueRO.GetPrevious<ArenaInput>();
                ref var avatar = ref avatars.GetRefRW(entity).ValueRW;

                var move = new FixedVector2(FixedPoint.FromFraction(current.moveX, 100), FixedPoint.FromFraction(current.moveY, 100));
                if (FixedMath.LengthSquared(move) > FixedPoint.One)
                {
                    move = FixedMath.Normalize(move);
                }
                if (move != FixedVector2.Zero)
                {
                    avatar.facing = FixedMath.Normalize(move);
                }
                avatar.velocity = FixedMath.MoveTowards(avatar.velocity, move * ArenaRules.MoveSpeed, ArenaRules.Acceleration * time.deltaTime);

                // Edges come from comparing with the previous tick, which is exact whatever the network did.
                var pressed = current.buttons & ~previous.buttons;
                if ((pressed & ArenaInput.DASH_BUTTON) != 0 && avatar.dashCooldown == 0)
                {
                    avatar.velocity += avatar.facing * ArenaRules.DashSpeed;
                    avatar.dashCooldown = ArenaRules.DASH_COOLDOWN_TICKS;
                }
                if ((pressed & ArenaInput.FIRE_BUTTON) != 0 && avatar.fireCooldown == 0)
                {
                    var origin = transforms[entity].position;
                    var offset = avatar.facing * (ArenaRules.AvatarRadius + ArenaRules.ProjectileRadius);
                    shots.Add(new Shot
                    {
                        owner = avatar.slot,
                        position = origin + new FixedVector3(offset.x, FixedPoint.Zero, offset.y),
                        velocity = avatar.facing * ArenaRules.ProjectileSpeed,
                    });
                    avatar.fireCooldown = ArenaRules.FIRE_COOLDOWN_TICKS;
                }

                for (var i = 0; i < commands.Length; i++)
                {
                    if (commands[i].TryGet<ArenaChangeColorCommand>(out var changeColor))
                    {
                        avatar.colorIndex = (changeColor.colorIndex % ArenaRules.COLOR_COUNT + ArenaRules.COLOR_COUNT) % ArenaRules.COLOR_COUNT;
                    }
                }
            }

            foreach (var shot in shots)
            {
                var projectile = state.EntityManager.CreateEntity(_projectileArchetype);
                state.EntityManager.SetComponentData(projectile, LockstepTransform.FromPosition(shot.position));
                state.EntityManager.SetComponentData(projectile, new ArenaProjectile
                {
                    ownerSlot = shot.owner,
                    velocity = shot.velocity,
                    ticksLeft = ArenaRules.PROJECTILE_LIFETIME_TICKS,
                });
            }
        }
    }
}
