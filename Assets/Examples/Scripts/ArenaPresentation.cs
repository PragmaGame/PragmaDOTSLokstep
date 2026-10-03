using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Examples
{
    /// <summary>
    /// Builds the arena and draws the scoreboard straight from the simulation world, which presentation code only reads.
    /// </summary>
    /// <remarks>
    /// The avatars and the projectiles are GameObject views: the package spawns them from <c>Views/ArenaViewConfig.asset</c>
    /// for the entities with an <c>EntityViewKey</c>, and returns them to its pool when the entities go away.
    /// </remarks>
    public sealed class ArenaPresentation : MonoBehaviour
    {
        private readonly List<(World World, LockstepClient Client)> _clients = new List<(World, LockstepClient)>();
        private readonly List<(int Slot, int Score, Color Color)> _scores = new List<(int, int, Color)>();

        private LockstepClient _client;
        private LockstepSimulation _simulation;
        private EntityQuery _avatarQuery;

        /// <summary>The session being displayed, if any.</summary>
        public LockstepClient Client => _client;

        private void Start()
        {
            BuildArena();
        }

        private void LateUpdate()
        {
            _scores.Clear();
            _client = FindDisplayedClient();
            var simulation = _client?.Simulation;
            if (simulation == null || !simulation.World.IsCreated)
            {
                _simulation = null;
                return;
            }

            // Queries belong to a simulation world, and every session has its own.
            if (simulation != _simulation)
            {
                _simulation = simulation;
                _avatarQuery = simulation.World.EntityManager.CreateEntityQuery(typeof(ArenaAvatar));
            }

            var avatars = _avatarQuery.ToComponentDataArray<ArenaAvatar>(Allocator.Temp);
            for (var i = 0; i < avatars.Length; i++)
            {
                _scores.Add((avatars[i].slot, avatars[i].score, ArenaColors.Get(avatars[i].colorIndex)));
            }
            _scores.Sort((a, b) => a.Slot.CompareTo(b.Slot));
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
