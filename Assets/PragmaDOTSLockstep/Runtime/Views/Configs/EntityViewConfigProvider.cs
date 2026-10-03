using UnityEngine;

namespace Pragma.Lockstep.Views
{
    /// <summary>Registers an <see cref="EntityViewConfig"/> in <see cref="EntityViewConfigs"/> while it is enabled.</summary>
    /// <remarks>
    /// Use it when presentation worlds are created on demand: a subscene loads only into the worlds that exist when it is
    /// enabled, a runtime catalog reaches every world.
    /// </remarks>
    [AddComponentMenu("Pragma/Lockstep/Entity View Config Provider")]
    public sealed class EntityViewConfigProvider : MonoBehaviour
    {
        [SerializeField] private EntityViewConfig _config;

        private EntityViewConfig _registered;
        // Tracked by hand: in edit mode a component counts as enabled although OnEnable never ran.
        private bool _isEnabled;

        /// <summary>The config to register; changing it while enabled swaps the registration.</summary>
        public EntityViewConfig Config
        {
            get => _config;
            set
            {
                _config = value;
                if (_isEnabled)
                {
                    Register();
                }
            }
        }

        private void OnEnable()
        {
            _isEnabled = true;
            Register();
        }

        private void OnDisable()
        {
            _isEnabled = false;
            EntityViewConfigs.Remove(_registered);
            _registered = null;
        }

        private void Register()
        {
            EntityViewConfigs.Remove(_registered);
            EntityViewConfigs.Add(_config);
            _registered = _config;
        }
    }
}
