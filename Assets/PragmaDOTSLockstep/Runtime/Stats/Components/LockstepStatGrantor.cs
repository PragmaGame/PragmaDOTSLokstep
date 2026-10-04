using Unity.Entities;

namespace Pragma.Lockstep.Stats
{
    /// <summary>
    /// An entity whose <see cref="LockstepStatGrant"/>s this entity receives: its player, its squad, the unit that carries
    /// it. Add it when the entity joins the group and remove it when it leaves; a grantor that no longer exists is dropped
    /// by <see cref="LockstepStatSystem"/>, and with it everything it gave.
    /// </summary>
    /// <remarks>
    /// A reference inside the simulation world, as the checksum hashes it by its target: never sort by it, hash it or send
    /// it. An entity created on the same tick can be named at once, also through a command buffer.
    /// </remarks>
    public struct LockstepStatGrantor : IBufferElementData
    {
        public Entity entity;

        public LockstepStatGrantor(Entity entity)
        {
            this.entity = entity;
        }
    }
}
