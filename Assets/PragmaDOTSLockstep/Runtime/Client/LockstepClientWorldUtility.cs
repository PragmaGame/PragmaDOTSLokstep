using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep
{
    /// <summary>Plumbing shared by the systems that own a <see cref="LockstepClient"/>.</summary>
    public static unsafe class LockstepClientWorldUtility
    {
        /// <summary>
        /// Hands the local input and queued commands (with their <see cref="LockstepCommandData"/>) of the world to the
        /// client, then clears the commands. An input singleton nobody wrote to (size zero) is skipped, so input set
        /// directly on the client is not overwritten.
        /// </summary>
        public static void PushLocalInput(EntityManager entityManager, EntityQuery localInputQuery, LockstepClient client)
        {
            if (localInputQuery.CalculateEntityCount() != 1)
            {
                return;
            }
            var entity = localInputQuery.GetSingletonEntity();
            var input = entityManager.GetComponentData<LockstepLocalInput>(entity);
            if (input.size > 0)
            {
                input.ApplyTo(client);
            }

            var commands = entityManager.GetBuffer<LockstepCommand>(entity);
            var hasData = entityManager.HasBuffer<LockstepCommandData>(entity);
            var data = hasData ? entityManager.GetBuffer<LockstepCommandData>(entity) : default;
            var dataLength = hasData ? data.Length : 0;
            var dataPointer = hasData ? (byte*)data.GetUnsafeReadOnlyPtr() : null;
            for (var i = 0; i < commands.Length; i++)
            {
                var command = commands[i];
                if (command.DataLength > 0 && command.DataOffset + command.DataLength > dataLength)
                {
                    Debug.LogError("[Lockstep] A command refers to data outside the LockstepCommandData buffer of the local input entity; it was not sent. " +
                                   "Create it with the buffer of the entity it is added to.");
                    continue;
                }
                client.AddCommand(command, dataPointer + command.DataOffset, command.DataLength);
            }
            commands.Clear();
            if (hasData)
            {
                data.Clear();
            }
        }

        /// <summary>
        /// Simulation options that copy this world's prefab registry and scene entities (<see cref="LockstepSceneEntity"/>)
        /// into every simulation world.
        /// </summary>
        public static LockstepSimulationOptions CreateSimulationOptions(World presentationWorld, bool waitForPrefabRegistry)
        {
            var options = new LockstepSimulationOptions
            {
                WorldName = $"Lockstep Simulation ({presentationWorld.Name})",
                Initialize = simulationWorld =>
                {
                    LockstepPrefabUtility.CopyRegistry(presentationWorld.EntityManager, simulationWorld.EntityManager);
                    LockstepPrefabUtility.CopySceneEntities(presentationWorld.EntityManager, simulationWorld.EntityManager);
                },
            };
            if (waitForPrefabRegistry)
            {
                options.CanCreate = () => presentationWorld.IsCreated && LockstepPrefabUtility.HasRegistry(presentationWorld.EntityManager);
            }
            return options;
        }
    }
}
