using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// Marks an entity baked into a subscene of the client (or local) world that every simulation world starts with: the
    /// buildings, resources and markers of a map. The built-in systems copy these entities into the simulation before
    /// tick 0, together with the prefab registry.
    /// </summary>
    /// <remarks>
    /// The copies are made in <see cref="order"/> order, so every client gets the same entities in the same chunk layout,
    /// whatever order its subscenes loaded in. <c>LockstepSceneEntityAuthoring</c> bakes the order from the identity of
    /// the GameObject in its scene. The copy keeps this component.
    /// </remarks>
    public struct LockstepSceneEntity : IComponentData
    {
        public ulong order;
    }
}
