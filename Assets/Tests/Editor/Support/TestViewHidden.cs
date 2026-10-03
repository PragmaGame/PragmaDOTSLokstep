using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    /// <summary>Enableable flag toggled by <see cref="TestViewSpawnSystem"/>; <see cref="TestVisibleViewDataUpdateSystem"/> skips entities that have it on.</summary>
    public struct TestViewHidden : IComponentData, IEnableableComponent
    {
    }
}
