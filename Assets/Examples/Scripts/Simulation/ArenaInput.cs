
namespace Pragma.Lockstep.Examples
{
    /// <summary>
    /// What one player sends every tick. Keep inputs small and quantized: the bytes of this struct are what travels
    /// and what the simulation reads, so the float stick value never reaches the simulation.
    /// </summary>
    public struct ArenaInput
    {
        public const byte FIRE_BUTTON = 1;
        public const byte DASH_BUTTON = 2;

        /// <summary>Stick direction in [-100, 100].</summary>
        public sbyte moveX;
        public sbyte moveY;
        public byte buttons;
        // Explicit, so no implicit padding byte travels with undefined content.
        public byte reserved;
    }
}
