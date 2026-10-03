using Pragma.Lockstep.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep.Examples
{
    /// <summary>Flies projectiles, knocks back the avatars they hit and scores for the shooter.</summary>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
    [UpdateAfter(typeof(ArenaMovementSystem))]
    [BurstCompile]
    public partial struct ArenaProjectileSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LockstepTime>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.GetSingleton<LockstepTime>().deltaTime;
            var hitDistance = ArenaRules.AvatarRadius + ArenaRules.ProjectileRadius;
            var avatarQuery = SystemAPI.QueryBuilder().WithAll<ArenaAvatar, LockstepTransform>().Build();
            var avatarEntities = avatarQuery.ToEntityArray(Allocator.Temp);
            var avatarTransforms = avatarQuery.ToComponentDataArray<LockstepTransform>(Allocator.Temp);
            var avatars = SystemAPI.GetComponentLookup<ArenaAvatar>();
            var expired = new NativeList<Entity>(Allocator.Temp);

            foreach (var (projectile, transform, entity) in SystemAPI.Query<RefRW<ArenaProjectile>, RefRW<LockstepTransform>>().WithEntityAccess())
            {
                ref var p = ref projectile.ValueRW;
                ref var t = ref transform.ValueRW;
                t.position += new FixedVector3(p.velocity.x, FixedPoint.Zero, p.velocity.y) * deltaTime;
                p.ticksLeft--;

                var hit = false;
                for (var i = 0; i < avatarEntities.Length && !hit; i++)
                {
                    var target = avatarEntities[i];
                    ref var victim = ref avatars.GetRefRW(target).ValueRW;
                    if (victim.slot == p.ownerSlot)
                    {
                        continue;
                    }
                    if (FixedMath.Distance(avatarTransforms[i].position.Xz, t.position.Xz) >= hitDistance)
                    {
                        continue;
                    }
                    victim.velocity += FixedMath.NormalizeSafe(p.velocity) * ArenaRules.Knockback;
                    hit = true;
                    for (var s = 0; s < avatarEntities.Length; s++)
                    {
                        ref var shooter = ref avatars.GetRefRW(avatarEntities[s]).ValueRW;
                        if (shooter.slot == p.ownerSlot)
                        {
                            shooter.score++;
                        }
                    }
                }

                var outside = FixedMath.Abs(t.position.x) > ArenaRules.HalfSize || FixedMath.Abs(t.position.z) > ArenaRules.HalfSize;
                if (hit || outside || p.ticksLeft <= 0)
                {
                    expired.Add(entity);
                }
            }

            state.EntityManager.DestroyEntity(expired.AsArray());
        }
    }
}
