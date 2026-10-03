using Unity.Entities;
using UnityEngine;

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
    public abstract class EntityComponentView<TData> : MonoBehaviour, IEntityComponentView<TData> where TData : IComponentData
    {
        private EntityView _view;

        /// <summary>The root view this part belongs to: its <see cref="EntityView.Entity"/> and <see cref="EntityView.Client"/>.</summary>
        protected EntityView View
        {
            get
            {
                if (_view == null)
                {
                    _view = GetComponentInParent<EntityView>(true);
                }
                return _view;
            }
        }

        /// <inheritdoc />
        public abstract void UpdateData(TData data);

        /// <summary>Activates or deactivates the part's GameObject.</summary>
        public virtual void SetViewEnable(bool value)
        {
            gameObject.SetActive(value);
        }

        /// <inheritdoc />
        public virtual void Bind()
        {
        }

        /// <inheritdoc />
        public virtual void BindBreak()
        {
        }
    }
}
