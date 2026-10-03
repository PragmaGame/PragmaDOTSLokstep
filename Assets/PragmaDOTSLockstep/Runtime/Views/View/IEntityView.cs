using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Views
{
    /// <summary>The root of a GameObject view of a simulation entity.</summary>
    public interface IEntityView
    {
        /// <summary>The root transform.</summary>
        Transform Transform { get; }

        /// <summary>The simulation entity shown, or <see cref="Entity.Null"/> while the view is not bound.</summary>
        Entity Entity { get; }

        /// <summary>The first part of type <typeparamref name="TView"/>, or default.</summary>
        TView GetComponentView<TView>() where TView : IEntityComponentView;

        /// <summary>Shows or hides the first part of type <typeparamref name="TView"/>.</summary>
        void SetComponentViewEnable<TView>(bool value) where TView : IEntityComponentView;

        /// <summary>Hands <paramref name="data"/> to the parts that show <typeparamref name="TData"/>.</summary>
        void UpdateData<TData>(TData data) where TData : IComponentData;

        /// <summary>Tells the parts the view got an entity.</summary>
        void Bind();

        /// <summary>Tells the parts the view is about to lose its entity.</summary>
        void BindBreak();
    }
}
