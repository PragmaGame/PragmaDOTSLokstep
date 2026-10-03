using System;
using UnityEngine;

namespace Pragma.Lockstep.Views
{
    /// <summary>Binds an <see cref="EntityViewKey"/> to the view prefab spawned for the entities that carry it.</summary>
    [Serializable]
    public struct EntityViewBinder
    {
        [field: SerializeField, Tooltip("The EntityViewKey of the entities this view shows; up to 29 bytes of UTF-8.")]
        public string Key { get; set; }

        [field: SerializeField]
        public EntityView View { get; set; }

        public EntityViewBinder(string key, EntityView view)
        {
            Key = key;
            View = view;
        }
    }
}
