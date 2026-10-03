using Pragma.Lockstep.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep.Examples
{
    /// <summary>Moves avatars, keeps them inside the arena and apart from each other.</summary>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
    [UpdateAfter(typeof(ArenaControlSystem))]
    [BurstCompile]
    public partial struct ArenaMovementSystem : ISystem
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
            var limit = ArenaRules.HalfSize - ArenaRules.AvatarRadius;

            foreach (var (avatar, transform) in SystemAPI.Query<RefRW<ArenaAvatar>, RefRW<LockstepTransform>>())
            {
                ref var a = ref avatar.ValueRW;
                ref var t = ref transform.ValueRW;
                t.position += new FixedVector3(a.velocity.x, FixedPoint.Zero, a.velocity.y) * deltaTime;

                // Bounce off the walls.
                if (FixedMath.Abs(t.position.x) > limit)
                {
                    t.position.x = FixedMath.Clamp(t.position.x, -limit, limit);
                    a.velocity.x = -a.velocity.x * ArenaRules.Friction;
                }
                if (FixedMath.Abs(t.position.z) > limit)
                {
                    t.position.z = FixedMath.Clamp(t.position.z, -limit, limit);
                    a.velocity.y = -a.velocity.y * ArenaRules.Friction;
                }

                t.rotation = FixedQuaternion.RotateY(FixedMath.Atan2(a.facing.x, a.facing.y));
                if (a.fireCooldown > 0)
                {
                    a.fireCooldown--;
                }
                if (a.dashCooldown > 0)
                {
                    a.dashCooldown--;
                }
            }

            // Push overlapping avatars apart; pairs are visited in query order, the same everywhere.
            var query = SystemAPI.QueryBuilder().WithAllRW<LockstepTransform>().WithAll<ArenaAvatar>().Build();
            var transforms = query.ToComponentDataArray<LockstepTransform>(Allocator.Temp);
            var minimum = ArenaRules.AvatarRadius * 2;
            for (var i = 0; i < transforms.Length; i++)
            {
                for (var j = i + 1; j < transforms.Length; j++)
                {
                    var a = transforms[i];
                    var b = transforms[j];
                    var delta = (b.position - a.position).Xz;
                    var distance = FixedMath.Length(delta);
                    if (distance >= minimum)
                    {
                        continue;
                    }
                    var normal = distance.rawValue == 0 ? FixedVector2.Right : delta / distance;
                    var push = (minimum - distance) * FixedPoint.Half;
                    a.position -= new FixedVector3(normal.x * push, FixedPoint.Zero, normal.y * push);
                    b.position += new FixedVector3(normal.x * push, FixedPoint.Zero, normal.y * push);
                    transforms[i] = a;
                    transforms[j] = b;
                }
            }
            query.CopyFromComponentDataArray(transforms);
        }
    }
}
