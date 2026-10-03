using System.Collections.Generic;
using Pragma.Lockstep.Views;

namespace Pragma.Lockstep.Tests
{
    /// <summary>Records the values it shows, the binds and the bind breaks; it shows changes only.</summary>
    public sealed class TestViewDataView : EntityComponentViewUnmanaged<TestViewData>
    {
        public readonly List<int> values = new List<int>();
        public int bindCount;
        public int bindBreakCount;

        public override void Bind()
        {
            base.Bind();
            bindCount++;
        }

        public override void BindBreak()
        {
            base.BindBreak();
            bindBreakCount++;
        }

        protected override void OnUpdateData(TestViewData data)
        {
            values.Add(data.value);
        }
    }
}
