using UnityEngine;

namespace Pragma.Lockstep.Examples
{
    /// <summary>The player colors; <see cref="ArenaAvatar.colorIndex"/> picks one.</summary>
    public static class ArenaColors
    {
        private static readonly Color[] Colors =
        {
            new Color(0.93f, 0.33f, 0.31f),
            new Color(0.27f, 0.55f, 0.95f),
            new Color(0.36f, 0.78f, 0.42f),
            new Color(0.98f, 0.78f, 0.25f),
            new Color(0.75f, 0.42f, 0.92f),
            new Color(0.25f, 0.82f, 0.84f),
        };

        public static Color Get(int index) => Colors[(index % Colors.Length + Colors.Length) % Colors.Length];
    }
}
