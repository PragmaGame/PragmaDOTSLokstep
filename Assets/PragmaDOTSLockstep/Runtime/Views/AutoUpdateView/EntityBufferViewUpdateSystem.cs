using Unity.Entities;

namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// Pushes the dynamic buffer of <typeparamref name="TElement"/> into the <see cref="IEntityBufferView{TElement}"/>
    /// parts of the views of the entities that have it. Declare one empty subclass per element type:
    /// <c>public partial class StatViewUpdateSystem : EntityBufferViewUpdateSystem&lt;LockstepStat&gt; { }</c>.
    /// </summary>
    /// <remarks>
    /// Pushes like <see cref="EntityViewUpdateSystem{TData}"/>: the buffers of the chunks where it was written since the
    /// previous push, and the buffers of the entities whose views are new. A part gets the buffer itself, read-only and
    /// valid during the call only.
    /// </remarks>
    public abstract partial class EntityBufferViewUpdateSystem<TElement> : EntityViewUpdateSystemBase where TElement : unmanaged, IBufferElementData
    {
        private BufferTypeHandle<TElement> _bufferHandle;
        private BufferAccessor<TElement> _chunkBuffers;

        private protected override ComponentType DataType => ComponentType.ReadOnly<TElement>();

        private protected override void UpdateHandles(EntityManager simulationManager)
        {
            _bufferHandle = simulationManager.GetBufferTypeHandle<TElement>(true);
        }

        private protected override void BeginChunk(in ArchetypeChunk chunk)
        {
            _chunkBuffers = chunk.GetBufferAccessorRO(ref _bufferHandle);
        }

        private protected override void PushFromChunk(EntityViewManager manager, int index, Entity entity)
        {
            manager.PushBuffer(entity, _chunkBuffers[index]);
        }

        private protected override void PushFromEntity(EntityViewManager manager, EntityManager simulationManager, Entity entity)
        {
            manager.PushBuffer(entity, simulationManager.GetBuffer<TElement>(entity, true));
        }
    }
}
