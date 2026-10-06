using Pragma.Lockstep.Mathematics;
using Pragma.Lockstep.Navigation;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Authoring
{
    /// <summary>
    /// Bakes a <see cref="LockstepNavAgent"/>, its empty path and its <see cref="LockstepNavVelocity"/>: entities of
    /// this prefab walk to destinations around the obstacles of the navigation grid and, with a radius, avoid each
    /// other while the world has a <see cref="LockstepNavAvoidance"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LockstepTransformAuthoring))]
    [AddComponentMenu("Pragma/Lockstep/Lockstep Nav Agent")]
    public sealed class LockstepNavAgentAuthoring : MonoBehaviour
    {
        [SerializeField, Min(0f), Tooltip("World units per second.")]
        private float _speed = 4f;

        [SerializeField, Min(0f), Tooltip("Degrees per second the agent turns to face where it walks; 0 turns at once.")]
        private float _angularSpeed;

        [SerializeField, Min(0f), Tooltip("The agent stops this close to the end of its path.")]
        private float _stoppingDistance;

        [SerializeField, Min(0f), Tooltip("Radius of the agent's body: overlapping agents are pushed apart and walking ones avoid each other; a body larger than the grid's agent radius keeps to cells with room for it. 0 neither pushes nor is pushed nor avoids.")]
        private float _radius;

        private sealed class Baker : Baker<LockstepNavAgentAuthoring>
        {
            public override void Bake(LockstepNavAgentAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new LockstepNavAgent
                {
                    speed = (FixedPoint)authoring._speed,
                    angularSpeed = FixedMath.ToRadians((FixedPoint)authoring._angularSpeed),
                    stoppingDistance = (FixedPoint)authoring._stoppingDistance,
                    radius = (FixedPoint)authoring._radius,
                });
                AddBuffer<LockstepNavWaypoint>(entity);
                AddComponent<LockstepNavVelocity>(entity);
            }
        }
    }
}
