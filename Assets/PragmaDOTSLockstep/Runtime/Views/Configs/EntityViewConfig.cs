using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// Catalog of GameObject views: which prefab shows the entities with a given <see cref="EntityViewKey"/>. It is kept
    /// apart from the prefab registry on purpose: the registry lists simulation prefabs every client needs, this lists
    /// graphics only the presentation needs; the key is the only link between them.
    /// </summary>
    /// <remarks>
    /// Bake it into the presentation world with <c>EntityViewConfigAuthoring</c> in a subscene, or register it at runtime
    /// with <see cref="EntityViewConfigProvider"/> or <see cref="EntityViewConfigs.Add"/>, which also reaches presentation
    /// worlds created after the scene loaded. When two binders share a key, the first one wins.
    /// </remarks>
    [CreateAssetMenu(fileName = nameof(EntityViewConfig), menuName = "Pragma/Lockstep/Entity View Config")]
    public sealed class EntityViewConfig : ScriptableObject
    {
        [SerializeField] private EntityViewBinder[] _binders = Array.Empty<EntityViewBinder>();

        /// <summary>Key-to-prefab bindings, in order of precedence.</summary>
        public IReadOnlyList<EntityViewBinder> Binders => _binders;

        /// <summary>Replaces the binders, for configs built in code; running managers pick the change up.</summary>
        public void SetBinders(params EntityViewBinder[] binders)
        {
            _binders = binders ?? Array.Empty<EntityViewBinder>();
            EntityViewConfigs.MarkChanged();
        }

        private void OnValidate()
        {
            // Edits made in play mode reach the running managers.
            EntityViewConfigs.MarkChanged();
        }
    }
}
