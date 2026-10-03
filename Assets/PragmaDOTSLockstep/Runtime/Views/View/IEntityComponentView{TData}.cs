using System;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Views
{
    /// <summary>An <see cref="IEntityComponentView"/> that receives the values of <typeparamref name="TData"/>.</summary>
    public interface IEntityComponentView<in TData> : IEntityComponentView where TData : IComponentData
    {
        /// <summary>The current value of the component for the entity the view shows.</summary>
        void UpdateData(TData data);

        Type IEntityComponentView.DataType => typeof(TData);

        void IEntityComponentView.UpdateData(IComponentData data)
        {
            if (data is not TData typed)
            {
                Debug.LogError($"[Lockstep] {GetType().Name} shows {typeof(TData).Name}, not {data?.GetType().Name ?? "null"}.");
                return;
            }
            UpdateData(typed);
        }
    }
}
