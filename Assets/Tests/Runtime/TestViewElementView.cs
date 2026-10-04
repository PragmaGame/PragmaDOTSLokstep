using System.Collections.Generic;
using Pragma.Lockstep.Views;
using Unity.Entities;

namespace Pragma.Lockstep.Tests
{
    /// <summary>Records the contents of every buffer pushed to it, as comma-separated values.</summary>
    public sealed class TestViewElementView : EntityBufferView<TestViewElement>
    {
        public readonly List<string> contents = new List<string>();

        public override void UpdateData(DynamicBuffer<TestViewElement> buffer)
        {
            var values = new string[buffer.Length];
            for (var i = 0; i < buffer.Length; i++)
            {
                values[i] = buffer[i].value.ToString();
            }
            contents.Add(string.Join(",", values));
        }
    }
}
