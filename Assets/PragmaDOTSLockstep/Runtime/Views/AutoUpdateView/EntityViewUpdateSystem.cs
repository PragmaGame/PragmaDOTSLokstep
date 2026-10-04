using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// Pushes the values of the simulation component <typeparamref name="TData"/> into the views of the entities that
    /// have it. Declare one empty subclass per component type:
    /// <c>public partial class HealthViewUpdateSystem : EntityViewUpdateSystem&lt;Health&gt; { }</c>.
    /// </summary>
    /// <remarks>
    /// Only the chunks whose <typeparamref name="TData"/> (or an enableable component of the query) changed since the
    /// previous push are read, judged by the change versions of the simulation world, plus the entities whose views were
    /// spawned, attached or forced since then, so a view always starts from the current values. Every write between two
    /// pushes is seen, also the ones made outside systems (the session entity's <see cref="LockstepTime"/>).
    /// </remarks>
    public abstract partial class EntityViewUpdateSystem<TData> : EntityViewUpdateSystemBase where TData : unmanaged, IComponentData
    {
        private ComponentTypeHandle<TData> _dataHandle;
        private NativeArray<TData> _chunkData;
        private bool _isZeroSized;

        private protected override ComponentType DataType => ComponentType.ReadOnly<TData>();

        protected override void OnCreate()
        {
            base.OnCreate();
            _isZeroSized = TypeManager.IsZeroSized(TypeManager.GetTypeIndex<TData>());
        }

        private protected override void UpdateHandles(EntityManager simulationManager)
        {
            _dataHandle = simulationManager.GetComponentTypeHandle<TData>(true);
        }

        private protected override void BeginChunk(in ArchetypeChunk chunk)
        {
            _chunkData = _isZeroSized ? default : chunk.GetNativeArray(ref _dataHandle);
        }

        private protected override void PushFromChunk(EntityViewManager manager, int index, Entity entity)
        {
            manager.PushData(entity, _isZeroSized ? default : _chunkData[index]);
        }

        private protected override void PushFromEntity(EntityViewManager manager, EntityManager simulationManager, Entity entity)
        {
            manager.PushData(entity, _isZeroSized ? default : simulationManager.GetComponentData<TData>(entity));
        }
    }
}
