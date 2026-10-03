using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// Default <see cref="IEntityViewPool"/>: one pool per presentation world that keeps released instances inactive
    /// under a root object and reuses them per prefab. Disposing it destroys every instance it created.
    /// </summary>
    /// <remarks>
    /// The root survives scene loads (in play mode), because a simulation world is not tied to a scene. Projectiles,
    /// effects and units come and go all the time; reusing instances avoids an Instantiate and a Destroy each.
    /// </remarks>
    public sealed class EntityViewPool : IEntityViewPool, IDisposable
    {
        private readonly Dictionary<EntityView, Stack<EntityView>> _free = new Dictionary<EntityView, Stack<EntityView>>();
        private readonly Dictionary<EntityView, EntityView> _prefabs = new Dictionary<EntityView, EntityView>();
        private readonly string _name;
        private readonly Transform _parent;
        private Transform _root;

        /// <param name="name">Name of the root object the instances live under.</param>
        /// <param name="parent">Optional parent of the root object.</param>
        public EntityViewPool(string name = "Entity Views", Transform parent = null)
        {
            _name = name;
            _parent = parent;
        }

        /// <summary>The object every instance lives under; created with the first instance.</summary>
        public Transform Root
        {
            get
            {
                if (_root == null)
                {
                    var root = new GameObject(_name);
                    if (_parent != null)
                    {
                        root.transform.SetParent(_parent, false);
                    }
                    else if (Application.isPlaying)
                    {
                        Object.DontDestroyOnLoad(root);
                    }
                    _root = root.transform;
                }
                return _root;
            }
        }

        /// <summary>Number of released instances waiting for reuse.</summary>
        public int InactiveCount
        {
            get
            {
                var count = 0;
                foreach (var free in _free.Values)
                {
                    count += free.Count;
                }
                return count;
            }
        }

        /// <inheritdoc />
        public EntityView Spawn(EntityView prefab)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }
            var view = Take(prefab);
            if (view == null)
            {
                view = Create(prefab);
            }
            view.gameObject.SetActive(true);
            return view;
        }

        /// <inheritdoc />
        public void Release(EntityView view)
        {
            if (view == null)
            {
                return;
            }
            if (!_prefabs.TryGetValue(view, out var prefab))
            {
                DestroyObject(view.gameObject);
                return;
            }
            view.gameObject.SetActive(false);
            var viewTransform = view.transform;
            viewTransform.SetParent(Root, false);
            viewTransform.localPosition = Vector3.zero;
            viewTransform.localRotation = Quaternion.identity;
            if (!_free.TryGetValue(prefab, out var free))
            {
                free = new Stack<EntityView>();
                _free.Add(prefab, free);
            }
            free.Push(view);
        }

        /// <summary>Creates inactive instances ahead of time, so the first spawns do not instantiate.</summary>
        public void Prewarm(EntityView prefab, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var view = Create(prefab);
                view.gameObject.SetActive(false);
                Release(view);
            }
        }

        /// <summary>Destroys every instance this pool created, the active ones included, and the root object.</summary>
        public void Dispose()
        {
            foreach (var view in _prefabs.Keys)
            {
                if (view != null)
                {
                    DestroyObject(view.gameObject);
                }
            }
            _prefabs.Clear();
            _free.Clear();
            if (_root != null)
            {
                DestroyObject(_root.gameObject);
            }
            _root = null;
        }

        internal static void DestroyObject(Object target)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }

        private EntityView Take(EntityView prefab)
        {
            if (!_free.TryGetValue(prefab, out var free))
            {
                return null;
            }
            // Instances destroyed from outside (a scene unload under a custom parent) are skipped and forgotten.
            while (free.Count > 0)
            {
                var view = free.Pop();
                if (view != null)
                {
                    return view;
                }
                _prefabs.Remove(view);
            }
            return null;
        }

        private EntityView Create(EntityView prefab)
        {
            var view = Object.Instantiate(prefab, Root, false);
            view.name = prefab.name;
            _prefabs.Add(view, prefab);
            return view;
        }
    }
}
