using Pragma.Lockstep.Mathematics;

namespace Pragma.Lockstep.Examples
{
    /// <summary>Tuning of the sample, written with exact fractions so every value is deterministic.</summary>
    public static class ArenaRules
    {
        public const int COLOR_COUNT = 6;
        public const int FIRE_COOLDOWN_TICKS = 10;
        public const int DASH_COOLDOWN_TICKS = 45;
        public const int PROJECTILE_LIFETIME_TICKS = 60;

        public static FixedPoint HalfSize => (FixedPoint)12;
        public static FixedPoint AvatarRadius => FixedPoint.Half;
        public static FixedPoint ProjectileRadius => FixedPoint.FromFraction(1, 5);
        public static FixedPoint MoveSpeed => (FixedPoint)7;
        public static FixedPoint Acceleration => (FixedPoint)40;
        public static FixedPoint DashSpeed => (FixedPoint)16;
        public static FixedPoint ProjectileSpeed => (FixedPoint)18;
        public static FixedPoint Knockback => (FixedPoint)9;
        public static FixedPoint Friction => FixedPoint.FromFraction(9, 10);
    }
}
