using Unity.Entities;

namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// Base of the MonoBehaviours that show one component type of the simulation entity. Put it on the
    /// <see cref="EntityView"/> GameObject or on a child.
    /// </summary>
    /// <remarks>
    /// <see cref="UpdateData"/> runs when a chunk holding the entity's <typeparamref name="TData"/> was written since the
    /// last update, so it may repeat a value; derive from <see cref="EntityComponentViewUnmanaged{TData}"/> to get
    /// changes only.
    /// </remarks>
    public abstract class EntityComponentView<TData> : EntityViewPart, IEntityComponentView<TData> where TData : IComponentData
    {
        /// <inheritdoc />
        public abstract void UpdateData(TData data);
    }
}
