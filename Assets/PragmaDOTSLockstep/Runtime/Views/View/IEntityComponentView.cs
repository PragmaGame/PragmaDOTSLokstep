using System;
using Unity.Entities;

namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// A part of an <see cref="EntityView"/> that shows one component type, or one dynamic buffer, of the simulation entity.
    /// </summary>
    public interface IEntityComponentView
    {
        /// <summary>The component type this part shows, or the element type of the buffer.</summary>
        Type DataType { get; }

        /// <summary>Boxed variant of the typed <c>UpdateData</c> of a component part.</summary>
        void UpdateData(IComponentData data);

        /// <summary>Shows or hides the part.</summary>
        void SetViewEnable(bool value);

        /// <summary>The view was bound to an entity (taken from the pool or attached).</summary>
        void Bind();

        /// <summary>
        /// The view is about to lose its entity (returned to the pool or detached); <see cref="EntityView.Entity"/> is still
        /// set. Restore here whatever the part changed on a pooled instance.
        /// </summary>
        void BindBreak();
    }
}
