using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;

namespace GravityPuzzle
{
    public static class ShredderCatchZonePool
    {
        private static GameObjectPool<ShredderCatchZone> pool;

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
}
