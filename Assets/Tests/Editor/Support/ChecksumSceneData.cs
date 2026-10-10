using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    /// <summary>A presentation-only shared component, like the render data the editor adds to baked entities.</summary>
    public struct ChecksumSceneData : ISharedComponentData
    {
        public ulong mask;
    }
}
