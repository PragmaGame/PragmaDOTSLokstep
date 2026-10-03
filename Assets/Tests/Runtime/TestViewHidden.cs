using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    /// <summary>
    /// Enableable tag of the view tests: the entities of <c>TestViewSpawnSystem</c> have it on from tick 8 to tick 12, and
    /// <c>TestVisibleViewDataUpdateSystem</c> skips them meanwhile.
    /// </summary>
    public struct TestViewHidden : IComponentData, IEnableableComponent
    {
    }
}
