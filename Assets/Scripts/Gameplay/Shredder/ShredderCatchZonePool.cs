using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;

namespace GravityPuzzle
{
    public static class ShredderCatchZonePool
    {
        private static GameObjectPool<ShredderCatchZone> pool;

        public static bool HasCapacity(int requiredCapacity)
        {
            return pool != null && pool.Capacity >= requiredCapacity;
        }

        public static void Configure(ShredderCatchZone prefab, Transform parent, int capacity)
        {
            if (prefab == null)
                return;

            pool = new GameObjectPool<ShredderCatchZone>(prefab, parent, capacity);
            pool.Prewarm();
        }

        public static bool TryRent(out ShredderCatchZone zone)
        {
            if (pool != null)
                return pool.TryRent(out zone);

            zone = null;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPool() => pool = null;
    }

    public static class ShredderFeedMaskPool
    {
        private static GameObjectPool<ShredderFeedMask> pool;

        public static void Configure(ShredderFeedMask prefab, Transform parent)
        {
            if (prefab == null)
                return;

            pool = new GameObjectPool<ShredderFeedMask>(prefab, parent, 1);
            pool.Prewarm();
        }

        public static bool TryRent(out ShredderFeedMask mask)
        {
            if (pool != null)
                return pool.TryRent(out mask);

            mask = null;
            return false;
        }

        public static void Return(ShredderFeedMask mask)
        {
            if (pool != null)
                pool.Return(mask);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPool() => pool = null;
    }
}
