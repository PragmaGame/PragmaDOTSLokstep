using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;

namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// Every frame, gives the views of entities with <see cref="LockstepTransform"/> the transform interpolated between the
    /// last two ticks, as the <see cref="LocalTransform"/> that <see cref="TransformComponentView"/> applies.
    /// </summary>
    /// <remarks>
    /// Interpolation needs <see cref="LockstepTransformPrevious"/> on the entity. A view shows the current transform until
    /// one tick after it was bound, so it never slides in from wherever the entity was created.
    /// </remarks>
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(EntityViewUpdateSystemGroup), OrderFirst = true)]
    public partial class TransformViewUpdateSystem : SystemBase
    {
        private readonly List<EntityView> _views = new List<EntityView>();

        protected override void OnUpdate()
        {
            if (!EntityViewManager.TryGet(World, out var manager))
            {
                return;
            }
            var simulation = manager.Simulation;
            if (simulation == null || !simulation.World.IsCreated || manager.ViewCount == 0)
            {
                return;
            }

            // A copy, so views attached or detached by the parts while they update do not break the iteration.
            _views.Clear();
            _views.AddRange(manager.SpawnedViews.Values);
            foreach (var attached in manager.AttachedViews.Values)
            {
                _views.AddRange(attached);
            }

            var simulationManager = simulation.World.EntityManager;
            var lastTick = simulation.Tick - 1;
            var alpha = manager.Client.InterpolationAlpha;
            for (var i = 0; i < _views.Count; i++)
            {
                var view = _views[i];
                if (view == null)
                {
                    // Destroyed from outside (a scene unload under a custom parent): the manager replaces it.
                    manager.RequestScan();
                    continue;
                }
                if (view.IsBound && view.IsAutoUpdateEnabled && view.IsHasHandler(typeof(LocalTransform)) &&
                    simulationManager.TryGetInterpolated(view.Entity, lastTick, alpha, view.BoundAtTick, out var transform))
                {
                    view.UpdateData(transform);
                }
            }
            _views.Clear();
        }
    }
}
