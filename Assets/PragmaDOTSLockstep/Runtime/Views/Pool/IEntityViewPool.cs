namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// Where <see cref="EntityViewManagerSystem"/> takes views and returns them. Each project plugs in its own pool
    /// (com.pragma.pool, Addressables, a DI container) through <see cref="EntityViewManagerSystem.PoolFactory"/>;
    /// <see cref="EntityViewPool"/> is the default.
    /// </summary>
    /// <remarks>
    /// The manager binds a view right after <see cref="Spawn"/> and unbinds it right before <see cref="Release"/>, so a
    /// pool only creates, activates, deactivates and keeps instances. Views spawned from a pool always go back to the
    /// same pool, also when the presentation world is destroyed.
    /// </remarks>
    public interface IEntityViewPool
    {
        /// <summary>An active instance of <paramref name="prefab"/>.</summary>
        EntityView Spawn(EntityView prefab);

        /// <summary>Takes back an instance returned by <see cref="Spawn"/>.</summary>
        void Release(EntityView view);
    }
}
