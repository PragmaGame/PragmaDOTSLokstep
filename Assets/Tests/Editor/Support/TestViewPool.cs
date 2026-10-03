using System;
using Pragma.Lockstep.Views;
using Object = UnityEngine.Object;

namespace Pragma.Lockstep.Tests
{
    /// <summary>A pool that never reuses anything and counts what the manager does with it.</summary>
    internal sealed class TestViewPool : IEntityViewPool, IDisposable
    {
        public int SpawnCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public bool IsDisposed { get; private set; }

        public EntityView Spawn(EntityView prefab)
        {
            SpawnCount++;
            var view = Object.Instantiate(prefab);
            view.gameObject.SetActive(true);
            return view;
        }

        public void Release(EntityView view)
        {
            ReleaseCount++;
            Object.DestroyImmediate(view.gameObject);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
