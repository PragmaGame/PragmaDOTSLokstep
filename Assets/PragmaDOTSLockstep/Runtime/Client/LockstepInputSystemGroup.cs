using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// Client-side group for the systems that gather local input and write <see cref="LockstepLocalInput"/>.
    /// It runs before the session update of the same frame.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.LocalSimulation,
        WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class LockstepInputSystemGroup : ComponentSystemGroup
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            var entity = EntityManager.CreateEntity(ComponentType.ReadWrite<LockstepLocalInput>(), ComponentType.ReadWrite<LockstepCommand>(),
                ComponentType.ReadWrite<LockstepCommandData>());
            EntityManager.SetName(entity, "LockstepLocalInput");
        }
    }
}
