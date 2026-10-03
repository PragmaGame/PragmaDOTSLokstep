using Pragma.Lockstep.Mathematics;
using Unity.Entities;

namespace Pragma.Lockstep.Examples
{
    public struct ArenaAvatar : IComponentData
    {
        public int slot;
        public FixedVector2 velocity;
        public FixedVector2 facing;
        public int score;
        public int colorIndex;
        public int fireCooldown;
        public int dashCooldown;
    }
}
