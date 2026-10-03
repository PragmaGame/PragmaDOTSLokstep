using System.Collections.Generic;
using UnityEngine;

namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// View catalogs registered at runtime. Every <see cref="EntityViewManagerSystem"/> uses them after the catalogs baked
    /// into its own world, so they also reach presentation worlds created on demand (a lobby, a match started from a menu).
    /// </summary>
    public static class EntityViewConfigs
    {
        private static readonly List<EntityViewConfig> Configs = new List<EntityViewConfig>();

        /// <summary>The registered configs, in registration order.</summary>
        public static IReadOnlyList<EntityViewConfig> All => Configs;

        /// <summary>Changes whenever a config is added, removed or edited; managers rebuild their catalog then.</summary>
        internal static int Version { get; private set; }

        /// <summary>Registers <paramref name="config"/>. Every call needs its own <see cref="Remove"/>.</summary>
        public static void Add(EntityViewConfig config)
        {
            if (config == null)
            {
                return;
            }
            Configs.Add(config);
            Version++;
        }

        /// <summary>Undoes one <see cref="Add"/>.</summary>
        public static void Remove(EntityViewConfig config)
        {
            if (config != null && Configs.Remove(config))
            {
                Version++;
            }
        }

        internal static void MarkChanged()
        {
            Version++;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Configs.Clear();
            Version++;
        }
    }
}
