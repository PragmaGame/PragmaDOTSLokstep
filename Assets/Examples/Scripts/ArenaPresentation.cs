using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Examples
{
    /// <summary>
    /// Draws the simulation with plain GameObjects: one capsule per avatar, one sphere per projectile, positions
    /// interpolated between ticks. Presentation only reads the simulation world, it never writes to it.
    /// </summary>
    /// <remarks>
    /// Projects that bake entity prefabs can use LockstepViewSystem instead, which spawns entity views from a
    /// LockstepPrefabRegistry automatically.
    /// </remarks>
    public sealed class ArenaPresentation : MonoBehaviour
    {
        private static readonly Color[] Colors =
        {
            new Color(0.93f, 0.33f, 0.31f),
            new Color(0.27f, 0.55f, 0.95f),
            new Color(0.36f, 0.78f, 0.42f),
            new Color(0.98f, 0.78f, 0.25f),
            new Color(0.75f, 0.42f, 0.92f),
            new Color(0.25f, 0.82f, 0.84f),
        };

        private readonly List<(World World, LockstepClient Client)> _clients = new List<(World, LockstepClient)>();
        private readonly Dictionary<Entity, Renderer> _avatars = new Dictionary<Entity, Renderer>();
        private readonly Dictionary<Entity, Transform> _projectiles = new Dictionary<Entity, Transform>();
        private readonly HashSet<Entity> _alive = new HashSet<Entity>();
        private readonly List<Entity> _stale = new List<Entity>();
        private readonly List<(int Slot, int Score, Color Color)> _scores = new List<(int, int, Color)>();

        private LockstepClient _client;
        private LockstepSimulation _simulation;
        private EntityQuery _avatarQuery;
        private EntityQuery _projectileQuery;
        private Transform _root;
        private MaterialPropertyBlock _properties;

        /// <summary>The session being displayed, if any.</summary>
        public LockstepClient Client => _client;

        private void Start()
        {
            _properties = new MaterialPropertyBlock();
            _root = new GameObject("Arena Views").transform;
            BuildArena();
        }

        private void LateUpdate()
        {
            _client = FindDisplayedClient();
            var simulation = _client?.Simulation;
            if (simulation == null || !simulation.World.IsCreated)
            {
                Clear();
                _simulation = null;
                return;
            }

            if (simulation != _simulation)
            {
                Clear();
                _simulation = simulation;
                var entityManager = simulation.World.EntityManager;
                _avatarQuery = entityManager.CreateEntityQuery(typeof(ArenaAvatar), typeof(LockstepTransform), typeof(LockstepTransformPrevious));
                _projectileQuery = entityManager.CreateEntityQuery(typeof(ArenaProjectile), typeof(LockstepTransform), typeof(LockstepTransformPrevious));
            }

            var lastTick = simulation.Tick - 1;
            var alpha = _client.InterpolationAlpha;
            SyncAvatars(lastTick, alpha);
            SyncProjectiles(lastTick, alpha);
        }

        private void OnGUI()
        {
            if (_simulation == null || _scores.Count == 0)
            {
                return;
            }
            GUILayout.BeginArea(new Rect(Screen.width - 190, 10, 180, 30 + _scores.Count * 22), GUI.skin.box);
            GUILayout.Label("Scores");
            foreach (var (slot, score, color) in _scores)
            {
                var previous = GUI.color;
                GUI.color = color;
                GUILayout.Label($"Player {slot + 1}{(slot == _client.LocalSlot ? " (you)" : "")}: {score}");
                GUI.color = previous;
            }
            GUILayout.EndArea();
        }

        private LockstepClient FindDisplayedClient()
        {
            _clients.Clear();
            LockstepWorlds.GetClients(_clients);
            foreach (var (_, client) in _clients)
            {
                if (client.Simulation != null)
                {
                    return client;
                }
            }
            return null;
        }

        private void SyncAvatars(int lastTick, float alpha)
        {
            var entities = _avatarQuery.ToEntityArray(Allocator.Temp);
            var avatars = _avatarQuery.ToComponentDataArray<ArenaAvatar>(Allocator.Temp);
            var current = _avatarQuery.ToComponentDataArray<LockstepTransform>(Allocator.Temp);
            var previous = _avatarQuery.ToComponentDataArray<LockstepTransformPrevious>(Allocator.Temp);

            _alive.Clear();
            _scores.Clear();
            for (var i = 0; i < entities.Length; i++)
            {
                if (!_avatars.TryGetValue(entities[i], out var view))
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    go.name = $"Avatar {avatars[i].slot + 1}";
                    go.transform.SetParent(_root, false);
                    Destroy(go.GetComponent<Collider>());
                    var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(nose.GetComponent<Collider>());
                    nose.transform.SetParent(go.transform, false);
                    nose.transform.localPosition = new Vector3(0, 0.3f, 0.45f);
                    nose.transform.localScale = new Vector3(0.25f, 0.25f, 0.4f);
                    view = go.GetComponent<Renderer>();
                    _avatars.Add(entities[i], view);
                }
                _alive.Add(entities[i]);

                var transform = LockstepTransformExtensions.Interpolate(current[i], previous[i], lastTick, alpha);
                view.transform.SetPositionAndRotation((Vector3)transform.Position + Vector3.up, transform.Rotation);
                var isLocal = avatars[i].slot == _client.LocalSlot;
                view.transform.localScale = isLocal ? new Vector3(1.1f, 1.1f, 1.1f) : Vector3.one;
                var color = Colors[avatars[i].colorIndex % Colors.Length];
                _properties.SetColor("_BaseColor", color);
                _properties.SetColor("_Color", color);
                view.SetPropertyBlock(_properties);
                _scores.Add((avatars[i].slot, avatars[i].score, color));
            }
            _scores.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            RemoveStale(_avatars);
        }

        private void SyncProjectiles(int lastTick, float alpha)
        {
            var entities = _projectileQuery.ToEntityArray(Allocator.Temp);
            var current = _projectileQuery.ToComponentDataArray<LockstepTransform>(Allocator.Temp);
            var previous = _projectileQuery.ToComponentDataArray<LockstepTransformPrevious>(Allocator.Temp);

            _alive.Clear();
            for (var i = 0; i < entities.Length; i++)
            {
                if (!_projectiles.TryGetValue(entities[i], out var view))
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = "Projectile";
                    Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(_root, false);
                    go.transform.localScale = Vector3.one * 0.4f;
                    view = go.transform;
                    _projectiles.Add(entities[i], view);
                }
                _alive.Add(entities[i]);
                var transform = LockstepTransformExtensions.Interpolate(current[i], previous[i], lastTick, alpha);
                view.position = (Vector3)transform.Position + Vector3.up;
            }
            RemoveStale(_projectiles);
        }

        private void RemoveStale<T>(Dictionary<Entity, T> views) where T : Component
        {
            _stale.Clear();
            foreach (var pair in views)
            {
                if (!_alive.Contains(pair.Key))
                {
                    _stale.Add(pair.Key);
                }
            }
            foreach (var entity in _stale)
            {
                if (views[entity] != null)
                {
                    Destroy(views[entity].gameObject);
                }
                views.Remove(entity);
            }
        }

        private void Clear()
        {
            foreach (var view in _avatars.Values)
            {
                if (view != null)
                {
                    Destroy(view.gameObject);
                }
            }
            foreach (var view in _projectiles.Values)
            {
                if (view != null)
                {
                    Destroy(view.gameObject);
                }
            }
            _avatars.Clear();
            _projectiles.Clear();
            _scores.Clear();
        }

        private void BuildArena()
        {
            var size = (float)ArenaRules.HalfSize * 2f;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Arena Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.localPosition = new Vector3(0, -0.05f, 0);
            floor.transform.localScale = new Vector3(size, 0.1f, size);
            for (var i = 0; i < 4; i++)
            {
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Arena Wall";
                wall.transform.SetParent(transform, false);
                var horizontal = i < 2;
                var sign = i % 2 == 0 ? 1f : -1f;
                wall.transform.localPosition = horizontal ? new Vector3(0, 0.5f, sign * (size / 2 + 0.25f)) : new Vector3(sign * (size / 2 + 0.25f), 0.5f, 0);
                wall.transform.localScale = horizontal ? new Vector3(size + 1f, 1f, 0.5f) : new Vector3(0.5f, 1f, size);
            }
        }
    }
}
