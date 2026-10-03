using System.Collections.Generic;
using Pragma.Lockstep.Views;

namespace Pragma.Lockstep.Tests
{
    /// <summary>Records every value pushed to it, repeated ones included.</summary>
    public sealed class TestViewDataRawView : EntityComponentView<TestViewData>
    {
        public readonly List<int> values = new List<int>();

        public override void UpdateData(TestViewData data)
        {
            values.Add(data.value);
        }
    }
}
