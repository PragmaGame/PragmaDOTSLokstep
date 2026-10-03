
namespace Pragma.Lockstep.Examples
{
    /// <summary>A command: discrete, never dropped, applied on the tick the server assigns to it.</summary>
    public struct ArenaChangeColorCommand
    {
        public int colorIndex;
    }
}
