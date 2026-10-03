using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// <see cref="EntityComponentView{TData}"/> that remembers the last value it showed and calls
    /// <see cref="OnUpdateData"/> only when the data changed.
    /// </summary>
    /// <remarks>
    /// Values are compared bytewise: the default equality of a struct without <c>IEquatable</c> uses reflection and boxes
    /// on every call. A struct with padding may report an equal value as changed now and then, never the reverse.
    /// </remarks>
    public abstract unsafe class EntityComponentViewUnmanaged<TData> : EntityComponentView<TData> where TData : unmanaged, IComponentData
    {
        private TData _lastData;
        private bool _isLastDataSet;

        public override void UpdateData(TData data)
        {
            var last = _lastData;
            if (_isLastDataSet && UnsafeUtility.MemCmp(&data, &last, sizeof(TData)) == 0)
            {
                return;
            }
            _lastData = data;
            _isLastDataSet = true;
            OnUpdateData(data);
        }

        /// <summary>
        /// Forgets the cached value. Without it a pooled view would carry the previous owner's state to the next entity:
        /// equal data would not call <see cref="OnUpdateData"/> and the old health bar or highlight would stay on screen.
        /// </summary>
        public override void Bind()
        {
            base.Bind();
            _lastData = default;
            _isLastDataSet = false;
        }

        protected abstract void OnUpdateData(TData data);
    }
}
