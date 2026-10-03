using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep.Views
{
    /// <summary>
    /// Says which GameObject view shows a simulation entity: the key of a binder in an <see cref="EntityViewConfig"/>.
    /// </summary>
    /// <remarks>
    /// Simulation data like any other component: set it when the entity is created (or bake it with
    /// <c>EntityViewKeyAuthoring</c>) and change it to swap the view. It is part of the state hash, so every client must
    /// set the same key.
    /// </remarks>
    public struct EntityViewKey : IComponentData
    {
        public FixedString32Bytes value;

        public EntityViewKey(in FixedString32Bytes value)
        {
            this.value = value;
        }

        /// <summary>False when <paramref name="text"/> is empty or longer than 29 bytes of UTF-8.</summary>
        public static bool TryCreate(string text, out EntityViewKey key)
        {
            key = default;
            return !string.IsNullOrEmpty(text) && key.value.CopyFromTruncated(text) == CopyError.None;
        }
    }
}
