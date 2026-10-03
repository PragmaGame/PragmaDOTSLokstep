using Pragma.Lockstep.Views;

namespace Pragma.Lockstep.Tests
{
    /// <summary>Pushes the tag <see cref="TestViewHidden"/>, a component without data.</summary>
    public partial class TestViewHiddenUpdateSystem : EntityViewUpdateSystem<TestViewHidden>
    {
    }
}
