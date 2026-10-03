using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// Root of the GameObject view of a simulation entity. Collects the <see cref="IEntityComponentView"/> parts on its
    /// GameObject and children and hands each part the values of its component type.
    /// </summary>
    /// <remarks>
    /// <see cref="EntityViewManagerSystem"/> takes views from the pool, binds them to simulation entities and returns
    /// them when the entities go away; the update systems push the data. A view only reads: nothing here writes to the
    /// simulation world, which is what keeps the presentation from causing desyncs.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Pragma/Lockstep/Entity View")]
    public class EntityView : MonoBehaviour, IEntityView
    {
        private Dictionary<Type, List<IEntityComponentView>> _componentViews;
        private Transform _transform;
        private bool _isAutoUpdateEnabled = true;

        /// <summary>The cached transform of the root.</summary>
        public Transform Transform
        {
            get
            {
                EnsureInitialized();
                return _transform;
            }
        }

        /// <summary>The simulation entity shown, or <see cref="Entity.Null"/> while the view is not bound.</summary>
        /// <remarks>Valid in this process and session only; commands refer to entities by <see cref="LockstepEntityId"/>.</remarks>
        public Entity Entity { get; private set; }

        /// <summary>The session whose simulation the view shows, or null while it is not bound.</summary>
        public LockstepClient Client { get; private set; }

        /// <summary>True while a manager shows an entity on this view (spawned or attached).</summary>
        public bool IsBound => Manager != null;

        /// <summary>True for views the caller owns and attached with <see cref="EntityViewManager.Attach"/>.</summary>
        public bool IsAttached { get; internal set; }

        /// <summary>
        /// When false the update systems skip this view (its transform included), for example while another system
        /// animates it. Turning it back on pushes the current values at the next update; unbinding turns it back on.
        /// </summary>
        public bool IsAutoUpdateEnabled
        {
            get => _isAutoUpdateEnabled;
            set
            {
                if (_isAutoUpdateEnabled == value)
                {
                    return;
                }
                _isAutoUpdateEnabled = value;
                if (value && Manager != null)
                {
                    Manager.ForceUpdate(Entity);
                }
            }
        }

        internal EntityViewManager Manager { get; private set; }

        /// <summary>The prefab the pool spawned this instance from.</summary>
        internal EntityView Prefab { get; set; }

        internal FixedString32Bytes Key { get; set; }

        /// <summary>Last simulated tick when the view was bound; it is not interpolated before the next tick.</summary>
        internal int BoundAtTick { get; private set; }

        internal int ScanStamp { get; set; }

        protected virtual void Awake()
        {
            EnsureInitialized();
        }

        /// <summary>The first part of type <typeparamref name="TView"/>, or default.</summary>
        public TView GetComponentView<TView>() where TView : IEntityComponentView
        {
            EnsureInitialized();
            foreach (var views in _componentViews.Values)
            {
                for (var i = 0; i < views.Count; i++)
                {
                    if (views[i] is TView typed)
                    {
                        return typed;
                    }
                }
            }
            return default;
        }

        /// <summary>True when every one of <paramref name="dataTypes"/> has a part.</summary>
        public bool IsHasHandlers(params Type[] dataTypes)
        {
            for (var i = 0; i < dataTypes.Length; i++)
            {
                if (!IsHasHandler(dataTypes[i]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>True when a part shows the component type <paramref name="type"/>.</summary>
        public bool IsHasHandler(Type type)
        {
            EnsureInitialized();
            return _componentViews.ContainsKey(type);
        }

        /// <summary>Shows or hides the first part of type <typeparamref name="TView"/>.</summary>
        public void SetComponentViewEnable<TView>(bool value) where TView : IEntityComponentView
        {
            var view = GetComponentView<TView>();
            if (view != null)
            {
                view.SetViewEnable(value);
            }
        }

        /// <summary>Hands <paramref name="data"/> to every part that shows <typeparamref name="TData"/>.</summary>
        public void UpdateData<TData>(TData data) where TData : IComponentData
        {
            EnsureInitialized();
            if (!_componentViews.TryGetValue(typeof(TData), out var views))
            {
                return;
            }
            for (var i = 0; i < views.Count; i++)
            {
                if (views[i] is IEntityComponentView<TData> typed)
                {
                    typed.UpdateData(data);
                }
            }
        }

        /// <summary>Boxed variant of <see cref="UpdateData{TData}"/> for tools that only have an <see cref="IComponentData"/>.</summary>
        public void UpdateData(IComponentData data)
        {
            if (data == null)
            {
                return;
            }
            EnsureInitialized();
            if (!_componentViews.TryGetValue(data.GetType(), out var views))
            {
                return;
            }
            for (var i = 0; i < views.Count; i++)
            {
                views[i].UpdateData(data);
            }
        }

        /// <summary>Tells the parts the view got an entity; the manager calls it.</summary>
        public void Bind()
        {
            EnsureInitialized();
            foreach (var views in _componentViews.Values)
            {
                for (var i = 0; i < views.Count; i++)
                {
                    views[i].Bind();
                }
            }
        }

        /// <summary>Tells the parts the view is about to lose its entity; the manager calls it.</summary>
        public void BindBreak()
        {
            EnsureInitialized();
            foreach (var views in _componentViews.Values)
            {
                for (var i = 0; i < views.Count; i++)
                {
                    views[i].BindBreak();
                }
            }
        }

        /// <summary>Collects the component views again, after parts were added or removed at runtime.</summary>
        public void RefreshComponentViews()
        {
            _componentViews = null;
            EnsureInitialized();
        }

        internal void BindTo(EntityViewManager manager, Entity entity, LockstepClient client, int tick)
        {
            Manager = manager;
            Entity = entity;
            Client = client;
            BoundAtTick = tick;
            Bind();
        }

        internal void Unbind()
        {
            // Unlinked before the parts hear about it, so a part that detaches the view or turns auto-update back on in
            // BindBreak does not re-enter. Entity and Client stay readable until the parts are done.
            Manager = null;
            IsAttached = false;
            BindBreak();
            Entity = Entity.Null;
            Client = null;
            // A pooled instance starts its next binding like a fresh one, not paused by its previous owner.
            _isAutoUpdateEnabled = true;
        }

        // Lazy rather than in Awake alone: Awake does not run for instances created in edit mode or spawned inactive.
        private void EnsureInitialized()
        {
            if (_componentViews != null)
            {
                return;
            }
            _transform = transform;
            _componentViews = new Dictionary<Type, List<IEntityComponentView>>();
            foreach (var view in GetComponentsInChildren<IEntityComponentView>(true))
            {
                // Parts of a nested view belong to that view.
                if (view is Component component && component.GetComponentInParent<EntityView>(true) != this)
                {
                    continue;
                }
                if (!_componentViews.TryGetValue(view.DataType, out var views))
                {
                    views = new List<IEntityComponentView>(1);
                    _componentViews.Add(view.DataType, views);
                }
                views.Add(view);
            }
        }
    }
}
