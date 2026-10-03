using Pragma.Lockstep.Views;
using UnityEngine;

namespace Pragma.Lockstep.Examples
{
    /// <summary>
    /// Paints the avatar in its player's color and draws the local player's avatar a little bigger. It shows
    /// <see cref="ArenaAvatar"/>, pushed by <see cref="ArenaAvatarViewUpdateSystem"/>; the TransformComponentView on the
    /// root moves it.
    /// </summary>
    public sealed class ArenaAvatarView : EntityComponentViewUnmanaged<ArenaAvatar>
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private Renderer[] _renderers;
        [SerializeField] private Transform _body;
        [SerializeField] private float _localPlayerScale = 1.1f;

        private MaterialPropertyBlock _properties;

        protected override void OnUpdateData(ArenaAvatar data)
        {
            _properties ??= new MaterialPropertyBlock();
            var color = ArenaColors.Get(data.colorIndex);
            _properties.SetColor(BaseColorId, color);
            _properties.SetColor(ColorId, color);
            foreach (var target in _renderers)
            {
                if (target != null)
                {
                    target.SetPropertyBlock(_properties);
                }
            }

            // Who the local player is lives here, in the presentation: the simulation is the same on every machine.
            var isLocal = View.Client != null && data.slot == View.Client.LocalSlot;
            _body.localScale = Vector3.one * (isLocal ? _localPlayerScale : 1f);
        }
    }
}
