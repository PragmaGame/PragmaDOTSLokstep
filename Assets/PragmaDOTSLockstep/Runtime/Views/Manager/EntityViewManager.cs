using System.Collections.Generic;
using Unity.Entities;

namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// Index from simulation entities to their GameObject views in one presentation world. It spawns and releases
    /// nothing: <see cref="EntityViewManagerSystem"/> does, through the pool.
    /// </summary>
    /// <remarks>
    /// The keys are entities of the simulation world this presentation world shows. They are valid in this process and
    /// session only; refer to entities across machines with <see cref="LockstepEntityId"/>.
    /// </remarks>
    public sealed class EntityViewManager
    {
        private readonly Dictionary<Entity, EntityView> _views = new Dictionary<Entity, EntityView>();
        private readonly Dictionary<Entity, List<EntityView>> _attached = new Dictionary<Entity, List<EntityView>>();
        private readonly List<Entity> _missing = new List<Entity>();
        private List<Entity> _pending = new List<Entity>();
        private List<Entity> _pendingNext = new List<Entity>();
        private int _attachedCount;

        /// <summary>Views spawned from the pool, by simulation entity.</summary>
        public IReadOnlyDictionary<Entity, EntityView> Views => _views;

        /// <summary>The session shown, or null.</summary>
        public LockstepClient Client { get; private set; }

        internal LockstepSimulation Simulation => Client?.Simulation;

        /// <summary>Counts the updates of the manager system, so update systems notice a frame they missed.</summary>
        internal int FrameIndex { get; private set; }

        /// <summary>Entities whose views get every value pushed this frame: new, attached or forced views.</summary>
        internal List<Entity> Pending => _pending;

        internal int ViewCount => _views.Count + _attachedCount;

        /// <summary>A view was found destroyed from outside: the manager system should compare again even without a tick.</summary>
        internal bool IsScanRequested { get; private set; }

        internal Dictionary<Entity, EntityView> SpawnedViews => _views;

        internal Dictionary<Entity, List<EntityView>> AttachedViews => _attached;

        /// <summary>The manager of a presentation world, if it has one.</summary>
        public static bool TryGet(World world, out EntityViewManager manager)
        {
            var system = world != null && world.IsCreated ? world.GetExistingSystemManaged<EntityViewManagerSystem>() : null;
            manager = system?.Manager;
            return manager != null;
        }

        /// <summary>The view spawned for <paramref name="entity"/>, if any.</summary>
        public bool TryGetView(Entity entity, out EntityView view)
        {
            return _views.TryGetValue(entity, out view) && view != null;
        }

        /// <summary>
        /// Shows <paramref name="entity"/> on a view the caller owns, for example a HUD panel in the scene. It gets the same
        /// data as spawned views until <see cref="Detach"/>, the entity goes away or the session ends, and is never
        /// returned to a pool. An entity can have any number of attached views besides its spawned one.
        /// </summary>
        /// <returns>False when no session is shown, the entity does not exist in it, or the view is already bound.</returns>
        public bool Attach(Entity entity, EntityView view)
        {
            var simulation = Simulation;
            if (view == null || view.IsBound || simulation == null || !simulation.World.IsCreated ||
                !simulation.World.EntityManager.Exists(entity))
            {
                return false;
            }
            if (!_attached.TryGetValue(entity, out var views))
            {
                views = new List<EntityView>(1);
                _attached.Add(entity, views);
            }
            views.Add(view);
            _attachedCount++;
            view.IsAttached = true;
            view.BindTo(this, entity, Client, simulation.Tick - 1);
            _pendingNext.Add(entity);
            return true;
        }

        /// <summary>Ends an <see cref="Attach"/>; the view keeps its last values and is left as it is.</summary>
        public void Detach(EntityView view)
        {
            if (view == null || !view.IsAttached || view.Manager != this)
            {
                return;
            }
            var entity = view.Entity;
            if (_attached.TryGetValue(entity, out var views) && views.Remove(view))
            {
                _attachedCount--;
                if (views.Count == 0)
                {
                    _attached.Remove(entity);
                }
            }
            view.Unbind();
        }

        /// <summary>Pushes every value of <paramref name="entity"/> to its views at the next update, changed or not.</summary>
        public void ForceUpdate(Entity entity)
        {
            _pendingNext.Add(entity);
        }

        internal void BeginFrame()
        {
            FrameIndex++;
            (_pending, _pendingNext) = (_pendingNext, _pending);
            _pendingNext.Clear();
        }

        internal void SetClient(LockstepClient client)
        {
            Client = client;
        }

        internal void RequestScan()
        {
            IsScanRequested = true;
        }

        internal void ClearScanRequest()
        {
            IsScanRequested = false;
        }

        internal void AddSpawned(Entity entity, EntityView view, int tick)
        {
            _views.Add(entity, view);
            view.BindTo(this, entity, Client, tick);
            _pending.Add(entity);
        }

        internal bool RemoveSpawned(Entity entity, out EntityView view)
        {
            if (!_views.Remove(entity, out view))
            {
                return false;
            }
            if (view != null)
            {
                view.Unbind();
            }
            return true;
        }

        internal void PushData<TData>(Entity entity, TData data) where TData : unmanaged, IComponentData
        {
            if (_views.TryGetValue(entity, out var view) && CanPush(view))
            {
                view.UpdateData(data);
            }
            if (_attachedCount == 0 || !_attached.TryGetValue(entity, out var views))
            {
                return;
            }
            for (var i = 0; i < views.Count; i++)
            {
                if (CanPush(views[i]))
                {
                    views[i].UpdateData(data);
                }
            }
        }

        internal void PushBuffer<TElement>(Entity entity, DynamicBuffer<TElement> buffer) where TElement : unmanaged, IBufferElementData
        {
            if (_views.TryGetValue(entity, out var view) && CanPush(view))
            {
                view.UpdateBuffer(buffer);
            }
            if (_attachedCount == 0 || !_attached.TryGetValue(entity, out var views))
            {
                return;
            }
            for (var i = 0; i < views.Count; i++)
            {
                if (CanPush(views[i]))
                {
                    views[i].UpdateBuffer(buffer);
                }
            }
        }

        /// <summary>A live view that is not paused gets data; a view destroyed from outside is reported.</summary>
        private bool CanPush(EntityView view)
        {
            if (view == null)
            {
                RequestScan();
                return false;
            }
            return view.IsAutoUpdateEnabled;
        }

        /// <summary>Detaches the views of entities that no longer exist and forgets attached views destroyed from outside.</summary>
        internal void DetachMissing(EntityManager simulationManager)
        {
            if (_attached.Count == 0)
            {
                return;
            }
            _missing.Clear();
            foreach (var pair in _attached)
            {
                if (!simulationManager.Exists(pair.Key))
                {
                    _missing.Add(pair.Key);
                    continue;
                }
                _attachedCount -= pair.Value.RemoveAll(view => view == null);
                if (pair.Value.Count == 0)
                {
                    _missing.Add(pair.Key);
                }
            }
            for (var i = 0; i < _missing.Count; i++)
            {
                DetachEntity(_missing[i]);
            }
        }

        internal void DetachAll()
        {
            if (_attached.Count == 0)
            {
                return;
            }
            _missing.Clear();
            _missing.AddRange(_attached.Keys);
            for (var i = 0; i < _missing.Count; i++)
            {
                DetachEntity(_missing[i]);
            }
        }

        private void DetachEntity(Entity entity)
        {
            if (!_attached.Remove(entity, out var views))
            {
                return;
            }
            _attachedCount -= views.Count;
            for (var i = 0; i < views.Count; i++)
            {
                if (views[i] != null)
                {
                    views[i].Unbind();
                }
            }
        }
    }
}
