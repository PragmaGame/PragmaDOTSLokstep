using Unity.Entities;

namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// Base of the MonoBehaviours that show one dynamic buffer of the simulation entity: its stats, its production queue.
    /// Put it on the <see cref="EntityView"/> GameObject or on a child, and declare an
    /// <see cref="EntityBufferViewUpdateSystem{TElement}"/> for the element type.
    /// </summary>
    /// <remarks>
    /// <see cref="UpdateData"/> runs when a chunk holding the entity's buffer was written since the last update, so it may
    /// repeat the contents: compare what the part shows before redrawing it. The buffer is valid during the call only.
    /// </remarks>
    public abstract class EntityBufferView<TElement> : EntityViewPart, IEntityBufferView<TElement> where TElement : unmanaged, IBufferElementData
    {
        /// <inheritdoc />
        public abstract void UpdateData(DynamicBuffer<TElement> buffer);
    }
}
