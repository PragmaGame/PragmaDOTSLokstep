using Unity.Entities;

namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// The systems that push simulation data into GameObject views. Runs in presentation worlds after
    /// <see cref="EntityViewManagerSystem"/> has spawned and returned the views of the frame.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation, WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(EntityViewManagerSystem))]
    public partial class EntityViewUpdateSystemGroup : ComponentSystemGroup
    {
    }
}
