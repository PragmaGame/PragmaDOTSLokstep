using Pragma.Lockstep.Views;

namespace Pragma.Lockstep.Tests
{
    /// <summary>Pushes <see cref="LockstepTime"/>, which the session writes outside systems before every tick.</summary>
    public partial class TestTimeViewUpdateSystem : EntityViewUpdateSystem<LockstepTime>
    {
    }
}
