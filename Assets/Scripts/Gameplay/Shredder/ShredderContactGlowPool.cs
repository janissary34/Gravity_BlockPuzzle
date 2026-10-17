using GravityPuzzle.Infrastructure.Pooling;
using GravityPuzzle.Presentation.Views;
using UnityEngine;

namespace GravityPuzzle
{
    public static class ShredderContactGlowPool
    {
        private static GameObjectPool<ShredderContactGlowView> pool;

        public static void Configure(ShredderContactGlowView prefab, Transform parent, int capacity = 4)
        {
            if (prefab == null)
                return;

            pool = new GameObjectPool<ShredderContactGlowView>(prefab, parent, Mathf.Max(1, capacity));
            pool.Prewarm();
        }

        public static bool TryRent(out ShredderContactGlowView view)
        {
            if (pool != null)
                return pool.TryRent(out view);

            view = null;
            return false;
        }

        public static void Return(ShredderContactGlowView view)
        {
            if (pool != null && view != null)
                pool.Return(view);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPool() => pool = null;
    }
}
