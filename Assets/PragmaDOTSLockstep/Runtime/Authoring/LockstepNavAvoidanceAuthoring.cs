using Pragma.Lockstep.Mathematics;
using Pragma.Lockstep.Navigation;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Authoring
{
    /// <summary>
    /// Bakes the <see cref="LockstepNavAvoidance"/> of a map: walking agents steer around each other instead of walking into
    /// each other, and paths go around crowds of standing agents. Put it into the subscene of the map, next to the
    /// navigation grid; remove it to turn avoidance off.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LockstepSceneEntityAuthoring))]
    [AddComponentMenu("Pragma/Lockstep/Lockstep Nav Avoidance")]
    public sealed class LockstepNavAvoidanceAuthoring : MonoBehaviour
    {
        [SerializeField, Min(0f), Tooltip("Seconds ahead an agent looks for collisions. Longer turns agents aside earlier and wider.")]
        private float _timeHorizon = 1.5f;

        [SerializeField, Min(0f), Tooltip("World units: agents farther than this (centre to centre) are not avoided.")]
        private float _neighbourDistance = 5f;

        [SerializeField, Range(0, LockstepNavAvoidance.MAX_NEIGHBOURS), Tooltip("How many of the nearest neighbours an agent avoids.")]
        private int _maxNeighbours = 8;

        [SerializeField, Min(0f), Tooltip("What a standing agent costs a path through its cell, in cells of walking: paths go around crowds of standing agents when the way around is shorter. Zero plans through them.")]
        private float _crowdCost = 4f;

        private sealed class Baker : Baker<LockstepNavAvoidanceAuthoring>
        {
            public override void Bake(LockstepNavAvoidanceAuthoring authoring)
            {
                AddComponent(GetEntity(TransformUsageFlags.None), new LockstepNavAvoidance
                {
                    timeHorizon = (FixedPoint)authoring._timeHorizon,
                    neighbourDistance = (FixedPoint)authoring._neighbourDistance,
                    maxNeighbours = authoring._maxNeighbours,
                    crowdCost = (FixedPoint)authoring._crowdCost,
                });
            }
        }
    }
}
