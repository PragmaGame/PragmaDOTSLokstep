using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace Pragma.Lockstep.Editor
{
    /// <summary>Live view of the lockstep sessions running in Play Mode, with replay export and state dumps.</summary>
    internal sealed class LockstepDebugWindow : EditorWindow
    {
        private readonly List<(World World, LockstepClient Client)> _clients = new List<(World, LockstepClient)>();
        private readonly List<(World World, LockstepServer Server)> _servers = new List<(World, LockstepServer)>();
        private Vector2 _scroll;

        [MenuItem("Window/Pragma/Lockstep Sessions")]
        private static void Open() => GetWindow<LockstepDebugWindow>("Lockstep");

        private void OnInspectorUpdate() => Repaint();

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see the running lockstep sessions.", MessageType.Info);
                return;
            }

            _clients.Clear();
            _servers.Clear();
            LockstepWorlds.GetClients(_clients);
            LockstepWorlds.GetServers(_servers);
            if (_clients.Count == 0 && _servers.Count == 0)
            {
                EditorGUILayout.HelpBox("No lockstep session is running.", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var (world, server) in _servers)
            {
                DrawServer(world, server);
            }
            foreach (var (world, client) in _clients)
            {
                DrawClient(world, client);
            }
            EditorGUILayout.EndScrollView();
        }

        private static void DrawServer(World world, LockstepServer server)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Server — {world.Name}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("State", server.State.ToString());
                EditorGUILayout.LabelField("Closed ticks", server.ClosedTicks.ToString());
                EditorGUILayout.LabelField("Players", $"{server.PlayerCount} / {server.Settings.MaxPlayers}");
                EditorGUILayout.LabelField("Verified checksums", server.VerifiedChecksumCount.ToString());
                EditorGUILayout.LabelField("Late inputs", server.LateInputCount.ToString());
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(server.State != LockstepServerState.Lobby))
                    {
                        if (GUILayout.Button("Start game"))
                        {
                            server.StartGame(world.Time.ElapsedTime);
                        }
                    }
                    if (GUILayout.Button("Export replay…"))
                    {
                        SaveReplay(server.ExportReplay(), world.Name);
                    }
                }
            }
        }

        private static void DrawClient(World world, LockstepClient client)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Client — {world.Name}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("State", client.State.ToString());
                EditorGUILayout.LabelField("Slot", client.LocalSlot.ToString());
                EditorGUILayout.LabelField("Ticks (simulated / confirmed)", $"{client.SimulatedTicks} / {client.ConfirmedTicks}");
                EditorGUILayout.LabelField("Last input tick", client.LastSentInputTick.ToString());
                EditorGUILayout.LabelField("Round trip", double.IsNaN(client.RoundTripTime) ? "—" : $"{client.RoundTripTime * 1000:0} ms");
                EditorGUILayout.LabelField("Jitter", $"{client.JitterTicks:0.00} ticks");
                if (client.IsDesynced)
                {
                    EditorGUILayout.HelpBox($"Desync detected at tick {client.DesyncTick}.", MessageType.Error);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!client.Settings.RecordReplay))
                    {
                        if (GUILayout.Button("Export replay…"))
                        {
                            SaveReplay(client.ExportReplay(), world.Name);
                        }
                    }
                    using (new EditorGUI.DisabledScope(client.Simulation == null))
                    {
                        if (GUILayout.Button("Log state hashes"))
                        {
                            LogStateHashes(world, client);
                        }
                    }
                }
            }
        }

        private static void SaveReplay(byte[] replay, string worldName)
        {
            var path = EditorUtility.SaveFilePanel("Export lockstep replay", "", $"{worldName}.lockstep", "lockstep");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllBytes(path, replay);
            }
        }

        // Per-component hashes of the simulation state: diff the output of two clients at the same tick to see what diverged.
        private static void LogStateHashes(World world, LockstepClient client)
        {
            var hashes = LockstepChecksum.ComputePerType(client.Simulation.World.EntityManager);
            var text = new StringBuilder();
            text.AppendLine($"[Lockstep] State hashes of {world.Name} after tick {client.SimulatedTicks - 1}:");
            foreach (var pair in hashes.OrderBy(pair => pair.Key))
            {
                text.AppendLine($"{pair.Value:X16}  {pair.Key}");
            }
            Debug.Log(text.ToString());
        }
    }
}
