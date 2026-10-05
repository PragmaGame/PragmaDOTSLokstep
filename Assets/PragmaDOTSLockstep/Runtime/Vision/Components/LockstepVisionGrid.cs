using Pragma.Lockstep.Mathematics;
using Unity.Entities;
using Unity.Mathematics;

namespace Pragma.Lockstep.Vision
{
    /// <summary>
    /// What every player slot sees, as a grid of cells on the XZ plane: a singleton in the simulation world. Its cells are
    /// the <see cref="LockstepVisionCell"/> buffer of the same entity, one plane of <see cref="CellCount"/> cells per slot.
    /// </summary>
    /// <remarks>
    /// <see cref="LockstepVisionSystem"/> fills the planes again on every tick from the <see cref="LockstepVisionSource"/>s:
    /// vision is state of the simulation, the same on every client, so gameplay may depend on it (a player orders attacks
    /// only on what it sees) and the presentation draws the fog of war from it. The cells are laid out as a
    /// <see cref="FixedGrid"/> (<see cref="Layout"/>). Bake the grid with <c>LockstepVisionGridAuthoring</c> into the subscene
    /// of the map, or create the entity in code before tick 0. Without a grid nothing is hidden: there is no fog.
    /// </remarks>
    public struct LockstepVisionGrid : IComponentData
    {
        /// <summary>World X and Z of the outer corner of cell (0, 0).</summary>
        public FixedVector2 origin;
        public FixedPoint cellSize;
        /// <summary>Number of cells along world X.</summary>
        public int width;
        /// <summary>Number of cells along world Z.</summary>
        public int height;
        /// <summary>Number of planes: the player slots of the session (<see cref="LockstepSessionInfo.maxPlayers"/>), set by <see cref="LockstepVisionSystem"/>.</summary>
        public int slotCount;
        /// <summary>
        /// Every slot sees the whole map, inside the grid and outside: the fog is off. Gameplay sets it (a match option, a
        /// debug switch, the end of the match); the planes are still filled.
        /// </summary>
        public bool isRevealed;

        /// <summary>The cells of one plane: the cell math lives there.</summary>
        public FixedGrid Layout => new FixedGrid(origin, cellSize, width, height);

        /// <summary>Number of cells of one plane.</summary>
        public int CellCount => Layout.CellCount;

        public bool IsValid => Layout.IsValid;

        public bool Contains(int2 cell) => Layout.Contains(cell);

        /// <summary>The cell that holds a world position (X and Z); it may lie outside the grid.</summary>
        public int2 WorldToCell(FixedVector2 position) => Layout.WorldToCell(position);
    }
}
