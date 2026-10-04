using UnityEngine;

namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// What the MonoBehaviour parts of an <see cref="EntityView"/> share: the view they belong to and the lifecycle calls.
    /// Derive from <see cref="EntityComponentView{TData}"/> or <see cref="EntityBufferView{TElement}"/>.
    /// </summary>
    public abstract class EntityViewPart : MonoBehaviour
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

        /// <summary>Activates or deactivates the part's GameObject.</summary>
        public virtual void SetViewEnable(bool value)
        {
            gameObject.SetActive(value);
        }

        /// <inheritdoc cref="IEntityComponentView.Bind"/>
        public virtual void Bind()
        {
        }

        /// <inheritdoc cref="IEntityComponentView.BindBreak"/>
        public virtual void BindBreak()
        {
        }
    }
}
