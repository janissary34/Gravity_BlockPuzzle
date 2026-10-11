using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;

namespace GravityPuzzle
{
    public static class ShredderPhysicsMouthPool
    {
        private static GameObjectPool<ShredderPhysicsMouth> pool;

        public static bool HasCapacity(int requiredCapacity)
        {
            return pool != null && pool.Capacity >= requiredCapacity;
        }

        public static void Configure(ShredderPhysicsMouth prefab, Transform parent, int capacity)
        {
            if (prefab == null)
                return;

            pool = new GameObjectPool<ShredderPhysicsMouth>(prefab, parent, capacity);
            pool.Prewarm();
        }

        public static bool TryRent(out ShredderPhysicsMouth mouth)
        {
            if (pool != null)
                return pool.TryRent(out mouth);

            mouth = null;
            return false;
        }

        public static void Return(ShredderPhysicsMouth mouth)
        {
            if (pool != null)
                pool.Return(mouth);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPool() => pool = null;
    }
}
