using System;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Views
{
    /// <summary>An <see cref="IEntityComponentView"/> that receives the dynamic buffer of <typeparamref name="TElement"/>.</summary>
    public interface IEntityBufferView<TElement> : IEntityComponentView where TElement : unmanaged, IBufferElementData
    {
        /// <summary>
        /// The current buffer of the entity the view shows. It is read-only and valid only during the call: copy what the
        /// part keeps.
        /// </summary>
        void UpdateData(DynamicBuffer<TElement> buffer);

        Type IEntityComponentView.DataType => typeof(TElement);

        void IEntityComponentView.UpdateData(IComponentData data)
        {
            Debug.LogError($"[Lockstep] {GetType().Name} shows the {typeof(TElement).Name} buffer, not {data?.GetType().Name ?? "null"}.");
        }
    }
}
